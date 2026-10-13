using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.Modules.Brands.Connections;

internal static class ConsentEndpoints
{
    public static Results<RedirectHttpResult, NotFound> Connect(
        string product,
        CurrentMember member,
        ConsentStates states,
        GoogleOAuth oauth,
        HttpRequest request)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        var consent = states.Create(member.BrandId, info.Product, member.UserId);
        return TypedResults.Redirect(oauth.AuthorizeUrl(info, consent, states.Protect(consent), oauth.RedirectUri(request)));
    }

    public static async Task<RedirectHttpResult> CallbackAsync(
        string? code,
        string? state,
        string? error,
        ClaimsPrincipal principal,
        ConsentStates states,
        GoogleOAuth oauth,
        IBrandAccess access,
        KairoDatabase database,
        BrandsDbContext db,
        ConnectionTokens tokens,
        GoogleRevocations revocations,
        IAuditLog audit,
        TimeProvider time,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (principal.FindUserId() is not { } userId)
        {
            return TypedResults.Redirect("/sign-in");
        }

        if (states.Unprotect(state) is not { } consent
            || consent.UserId != userId
            || await access.FindRolesAsync(consent.BrandId, userId, cancellationToken) is not { } roles
            || !roles.HasFlag(BrandRoles.Admin))
        {
            return TypedResults.Redirect("/");
        }

        await database.UseBrandAsync(consent.BrandId, cancellationToken);
        var handle = await db.Brands.Select(brand => brand.Handle).SingleAsync(cancellationToken);
        var settings = $"/{handle}/settings/connections";
        var now = time.GetUtcNow();
        if (consent.ExpiresAt <= now)
        {
            return Failed(settings, "expired");
        }

        if (!await UseNonceAsync(db, consent, now, cancellationToken))
        {
            return Failed(settings, "failed");
        }

        if (error is not null)
        {
            return Failed(settings, error == "access_denied" ? "cancelled" : "failed");
        }

        var product = Products.Get(consent.Product);
        var grant = string.IsNullOrEmpty(code)
            ? null
            : await oauth.ExchangeAsync(code, consent.Verifier, oauth.RedirectUri(request), cancellationToken);
        if (grant is not { RefreshToken: { } refreshToken, Subject: { } subject })
        {
            return Failed(settings, "failed");
        }

        if (product.Scopes.Except(grant.Scopes).Any())
        {
            await revocations.RevokeIfUnusedAsync(subject, refreshToken, cancellationToken);
            return Failed(settings, "scopes_missing");
        }

        (string Subject, string Token)? replaced = null;
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var connection = await ConnectionLifecycle.FindAsync(db, product.Product, cancellationToken);
            var before = connection is null ? null : ConnectionLifecycle.Snapshot(connection);
            if (connection is not null && await tokens.OpenForRevocationAsync(connection, cancellationToken) is { } oldToken)
            {
                replaced = (connection.GoogleSubject, oldToken);
            }

            if (connection is null)
            {
                connection = new Connection { BrandId = consent.BrandId, Product = product.Product };
                db.Connections.Add(connection);
            }

            connection.State = ConnectionState.ChoosingAccount;
            connection.ExternalId = null;
            connection.DisplayName = null;
            connection.ManagerId = null;
            connection.Scopes = string.Join(' ', product.Scopes);
            connection.GoogleSubject = subject;
            connection.ConnectedBy = userId;
            connection.ConnectedByName = principal.FindFirstValue(KairoClaims.Name) ?? "";
            connection.ConnectedAt = now;
            connection.CheckedAt = null;
            connection.ReconnectReason = null;
            await tokens.SealAsync(connection, refreshToken, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord("connection.connected", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
                cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (replaced is { } previous)
        {
            await revocations.RevokeIfUnusedAsync(previous.Subject, previous.Token, cancellationToken);
        }

        return TypedResults.Redirect($"{settings}?choose={product.Slug}");
    }

    private static async Task<bool> UseNonceAsync(BrandsDbContext db, ConsentState consent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.ConsentNonces.Where(nonce => nonce.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        var used = new ConsentNonce { Nonce = consent.Nonce, ExpiresAt = consent.ExpiresAt };
        db.ConsentNonces.Add(used);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(used).State = EntityState.Detached;
            return false;
        }
    }

    private static RedirectHttpResult Failed(string settings, string code) => TypedResults.Redirect($"{settings}?error={code}");
}
