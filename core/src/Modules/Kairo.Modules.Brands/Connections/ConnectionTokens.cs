using System.Security.Cryptography;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Modules.Brands.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Brands.Connections;

internal static class ReconnectReasons
{
    public const string TokenRejected = "token_rejected";
    public const string TokenUnreadable = "token_unreadable";
    public const string AdminLeft = "admin_left";
}

internal sealed class ConnectionTokens(
    BrandsDbContext db,
    ITokenCipher cipher,
    GoogleOAuth oauth,
    AccessTokenCache cache,
    IAuditLog audit,
    ILiveUpdates live,
    IServiceScopeFactory scopes,
    ILogger<ConnectionTokens> logger)
{
    public async Task SealAsync(Connection connection, string refreshToken, CancellationToken cancellationToken)
    {
        var sealedToken = await cipher.SealAsync(refreshToken, Context(connection), cancellationToken);
        connection.RefreshToken = sealedToken.Ciphertext;
        connection.KeyVersion = sealedToken.KeyVersion;
    }

    public async Task<string?> OpenAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            return null;
        }

        if (!cipher.CanOpen(connection.KeyVersion))
        {
            throw new InvalidOperationException(
                $"The {connection.Product} connection's token was sealed with key {connection.KeyVersion}, which this service isn't configured to use.");
        }

        try
        {
            return await cipher.OpenAsync(connection.RefreshToken, Context(connection), cancellationToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<string?> OpenForRevocationAsync(Connection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await OpenAsync(connection, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not decrypt the {Product} connection's token, so it can't be revoked at Google.", connection.Product);
            return null;
        }
    }

    public async Task<AccessToken?> GetAsync(Connection connection, bool fresh, CancellationToken cancellationToken)
    {
        if (!fresh && cache.Find(connection) is { } cached)
        {
            return cached;
        }

        if (await OpenAsync(connection, cancellationToken) is not { } refreshToken)
        {
            await LoseOnItsOwnAsync(connection, ReconnectReasons.TokenUnreadable, cancellationToken);
            return null;
        }

        if (await oauth.RefreshAsync(refreshToken, cancellationToken) is not { } grant)
        {
            await LoseOnItsOwnAsync(connection, ReconnectReasons.TokenRejected, cancellationToken);
            return null;
        }

        var token = new AccessToken(grant.AccessToken, grant.ExpiresAt);
        cache.Store(connection, token);
        return token;
    }

    public async Task LoseAsync(Connection connection, string reason, CancellationToken cancellationToken)
    {
        var before = ConnectionLifecycle.Snapshot(connection);
        connection.State = ConnectionState.NeedsReconnect;
        connection.RefreshToken = null;
        connection.KeyVersion = null;
        connection.ReconnectReason = reason;
        cache.Forget(connection.Id);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("connection.needs_reconnect", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
            cancellationToken);
        await live.PublishAsync(ConnectionEvents.Changed(connection.Product), cancellationToken);
    }

    private async Task LoseOnItsOwnAsync(Connection connection, string reason, CancellationToken cancellationToken)
    {
        cache.Forget(connection.Id);
        await using (var scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(connection.BrandId, cancellationToken);
            var own = scope.ServiceProvider.GetRequiredService<BrandsDbContext>();
            var current = await own.Connections.SingleOrDefaultAsync(
                row => row.Id == connection.Id && row.ConnectedAt == connection.ConnectedAt && row.RefreshToken != null,
                cancellationToken);
            if (current is not null)
            {
                await scope.ServiceProvider.GetRequiredService<ConnectionTokens>().LoseAsync(current, reason, cancellationToken);
            }
        }

        await db.Entry(connection).ReloadAsync(cancellationToken);
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";
}
