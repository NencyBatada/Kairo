using Kairo.Contracts.Audit;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal static class MemberEndpoints
{
    public static void MapMembers(this IEndpointRouteBuilder api)
    {
        var members = api.MapBrandApi().MapGroup("/members");
        members.MapGet("", ListAsync);
        members.MapPut("/{userId:guid}/roles", ChangeRolesAsync).RequireBrandRole(BrandRoles.Admin);
        members.MapDelete("/{userId:guid}", RemoveAsync);
    }

    private static async Task<Ok<MemberResponse[]>> ListAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Memberships
            .Join(db.Users, membership => membership.UserId, user => user.Id, (membership, user) => new
            {
                membership.UserId,
                user.Name,
                user.Email,
                user.PictureUrl,
                membership.Roles,
                membership.JoinedAt,
            })
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(row => new MemberResponse(row.UserId, row.Name, row.Email, row.PictureUrl, row.Roles.ToList(), row.JoinedAt))
            .ToArray());
    }

    private static async Task<Results<Ok<MemberResponse>, ValidationProblem, NotFound, ProblemHttpResult>> ChangeRolesAsync(
        Guid userId,
        ChangeRolesRequest request,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (request.Roles is not { Length: > 0 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Choose at least one role."] });
        }

        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        var before = membership.Roles;
        var after = request.Roles.ToFlags();
        if (before.HasFlag(BrandRoles.Admin) && !after.HasFlag(BrandRoles.Admin)
            && await TeamRules.IsLastAdminAsync(db, userId, cancellationToken))
        {
            return Conflict(TeamRules.LastAdmin);
        }

        if (before.HasFlag(BrandRoles.Approver) && !after.HasFlag(BrandRoles.Approver))
        {
            if (await TeamRules.SoleDutyAsync(db, userId, cancellationToken) is { } sole)
            {
                return Conflict(sole);
            }

            db.DutyAssignments.RemoveRange(await db.DutyAssignments.Where(a => a.UserId == userId).ToListAsync(cancellationToken));
        }

        membership.Roles = after;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("member.roles_changed", "member", userId.ToString(), new { Roles = before.ToList() }, new { Roles = after.ToList() }),
            cancellationToken);

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        return TypedResults.Ok(new MemberResponse(user.Id, user.Name, user.Email, user.PictureUrl, after.ToList(), membership.JoinedAt));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid userId,
        CurrentMember member,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var leaving = userId == member.UserId;
        if (!leaving && !member.Has(BrandRoles.Admin))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only admins can remove other members.");
        }

        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        if (membership.Roles.HasFlag(BrandRoles.Admin) && await TeamRules.IsLastAdminAsync(db, userId, cancellationToken))
        {
            return Conflict(TeamRules.LastAdmin);
        }

        if (await TeamRules.SoleDutyAsync(db, userId, cancellationToken) is { } sole)
        {
            return Conflict(sole);
        }

        db.DutyAssignments.RemoveRange(await db.DutyAssignments.Where(a => a.UserId == userId).ToListAsync(cancellationToken));
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(leaving ? "member.left" : "member.removed", "member", userId.ToString(), new { Roles = membership.Roles.ToList() }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Conflict(string message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: message);
}

internal sealed record MemberResponse(Guid UserId, string Name, string Email, string? PictureUrl, BrandRole[] Roles, DateTimeOffset JoinedAt);

internal sealed record ChangeRolesRequest(BrandRole[] Roles);
