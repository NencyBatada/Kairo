using System.Security.Cryptography;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

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
    IAuditLog audit)
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

        try
        {
            return await cipher.OpenAsync(connection.RefreshToken, Context(connection), cancellationToken);
        }
        catch (CryptographicException)
        {
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
            await LoseAsync(connection, ReconnectReasons.TokenUnreadable, cancellationToken);
            return null;
        }

        if (await oauth.RefreshAsync(refreshToken, cancellationToken) is not { } grant)
        {
            await LoseAsync(connection, ReconnectReasons.TokenRejected, cancellationToken);
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
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";
}
