using System.Net.Mail;
using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Members;

namespace Kairo.Modules.Identity.Invitations;

internal static class InvitationEndpoints
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static void MapInvitations(this IEndpointRouteBuilder api)
    {
        var sent = api.MapBrandApi().MapGroup("/invitations").RequireBrandRole(BrandRoles.Admin);
        sent.MapGet("", ListPendingAsync);
        sent.MapPost("", InviteAsync);
        sent.MapPost("/{invitationId:guid}/revoke", RevokeAsync);

        var mine = api.MapGroup("/me/invitations").RequireAuthorization();
        mine.MapGet("", ListMineAsync);
        mine.MapPost("/{invitationId:guid}/accept", AcceptAsync);
        mine.MapPost("/{invitationId:guid}/decline", DeclineAsync);
    }

    private static async Task<Ok<PendingInvitation[]>> ListPendingAsync(
        IdentityDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await db.Invitations
            .Where(invitation => invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now)
            .Join(db.Users, invitation => invitation.InvitedBy, user => user.Id, (invitation, user) => new
            {
                invitation.Id,
                invitation.Email,
                invitation.Roles,
                InvitedBy = user.Name,
                invitation.SentAt,
                invitation.ExpiresAt,
            })
            .OrderBy(row => row.Email)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(row => new PendingInvitation(row.Id, row.Email, row.Roles.ToList(), row.InvitedBy, row.SentAt, row.ExpiresAt))
            .ToArray());
    }

    private static async Task<Results<Created<PendingInvitation>, ValidationProblem>> InviteAsync(
        InviteRequest request,
        CurrentMember member,
        IdentityDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var errors = new Dictionary<string, string[]>();
        if (email is null)
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (request.Roles is not { Length: > 0 })
        {
            errors["roles"] = ["Choose at least one role."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var alreadyMember = await db.Memberships
            .Join(db.Users, membership => membership.UserId, user => user.Id, (_, user) => user.Email)
            .AnyAsync(memberEmail => memberEmail == email, cancellationToken);
        if (alreadyMember)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["That person is already a member."] });
        }

        var now = time.GetUtcNow();
        var invitation = await db.Invitations.SingleOrDefaultAsync(
            existing => existing.Email == email && existing.Status == InvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            invitation = new Invitation { Email = email!, Status = InvitationStatus.Pending };
            db.Invitations.Add(invitation);
        }

        invitation.Roles = request.Roles.ToFlags();
        invitation.InvitedBy = member.UserId;
        invitation.SentAt = now;
        invitation.ExpiresAt = now + Lifetime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.sent", "invitation", invitation.Id.ToString(), After: new { invitation.Email, Roles = invitation.Roles.ToList() }),
            cancellationToken);

        var inviter = await db.Users.Where(user => user.Id == member.UserId).Select(user => user.Name).SingleAsync(cancellationToken);
        return TypedResults.Created(
            $"/api/brands/{member.BrandId}/invitations/{invitation.Id}",
            new PendingInvitation(invitation.Id, invitation.Email, invitation.Roles.ToList(), inviter, invitation.SentAt, invitation.ExpiresAt));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid invitationId,
        IdentityDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(
            pending => pending.Id == invitationId && pending.Status == InvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.revoked", "invitation", invitation.Id.ToString(), Before: new { invitation.Email }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MyInvitation[]>> ListMineAsync(
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IBrandDirectory brands,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = await EmailOfAsync(principal, db, cancellationToken);
        var now = time.GetUtcNow();
        List<InvitationRow> rows;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            rows = await db.Invitations
                .Where(invitation => invitation.Email == email && invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now)
                .Join(db.Users, invitation => invitation.InvitedBy, user => user.Id, (invitation, user) => new InvitationRow(invitation.Id, invitation.BrandId, invitation.Roles, user.Name, invitation.ExpiresAt))
                .ToListAsync(cancellationToken);
        }

        var workspaces = (await brands.GetAsync(rows.Select(row => row.BrandId).Distinct().ToList(), cancellationToken))
            .ToDictionary(workspace => workspace.Id);
        return TypedResults.Ok(rows
            .Where(row => workspaces.ContainsKey(row.BrandId))
            .Select(row => new MyInvitation(
                row.Id,
                workspaces[row.BrandId].Name,
                workspaces[row.BrandId].Handle,
                row.Roles.ToList(),
                row.InvitedBy,
                row.ExpiresAt))
            .ToArray());
    }

    private static async Task<Results<Ok<JoinedWorkspace>, NotFound>> AcceptAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IBrandDirectory brands,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await FindMineAsync(invitationId, principal, db, database, time, cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        var userId = principal.FindUserId()!.Value;
        var now = time.GetUtcNow();
        await database.UseBrandAsync(invitation.BrandId, cancellationToken);
        if (!await db.Memberships.AnyAsync(membership => membership.UserId == userId, cancellationToken))
        {
            db.Memberships.Add(new Membership
            {
                BrandId = invitation.BrandId,
                UserId = userId,
                Roles = invitation.Roles,
                JoinedAt = now,
            });
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.RespondedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.accepted", "invitation", invitation.Id.ToString(), After: new { invitation.Email, Roles = invitation.Roles.ToList() }),
            cancellationToken);

        var workspace = (await brands.GetAsync([invitation.BrandId], cancellationToken)).Single();
        return TypedResults.Ok(new JoinedWorkspace(workspace.Id, workspace.Handle));
    }

    private static async Task<Results<NoContent, NotFound>> DeclineAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await FindMineAsync(invitationId, principal, db, database, time, cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        await database.UseBrandAsync(invitation.BrandId, cancellationToken);
        invitation.Status = InvitationStatus.Declined;
        invitation.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.declined", "invitation", invitation.Id.ToString(), Before: new { invitation.Email }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Invitation?> FindMineAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = await EmailOfAsync(principal, db, cancellationToken);
        var now = time.GetUtcNow();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Invitations.SingleOrDefaultAsync(
                invitation => invitation.Id == invitationId
                    && invitation.Email == email
                    && invitation.Status == InvitationStatus.Pending
                    && invitation.ExpiresAt > now,
                cancellationToken);
        }
    }

    private static Task<string> EmailOfAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.FindUserId()!.Value;
        return db.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync(cancellationToken);
    }

    private static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        return email is { Length: > 0 and <= 320 } && MailAddress.TryCreate(email, out var address) && address.Address == email
            ? email
            : null;
    }
}

internal sealed record InviteRequest(string Email, BrandRole[] Roles);

internal sealed record PendingInvitation(Guid Id, string Email, BrandRole[] Roles, string InvitedBy, DateTimeOffset SentAt, DateTimeOffset ExpiresAt);

internal sealed record MyInvitation(Guid Id, string WorkspaceName, string WorkspaceHandle, BrandRole[] Roles, string InvitedBy, DateTimeOffset ExpiresAt);

internal sealed record JoinedWorkspace(Guid Id, string Handle);

internal sealed record InvitationRow(Guid Id, Guid BrandId, BrandRoles Roles, string InvitedBy, DateTimeOffset ExpiresAt);
