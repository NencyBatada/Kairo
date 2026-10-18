using System.Security.Cryptography;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Brands.Data;
using Microsoft.Extensions.Logging;
using Kairo.SharedKernel.Observability;

namespace Kairo.Modules.Brands.Connections;

internal static class ReconnectReasons
{
    public const string TokenRejected = "token_rejected";
    public const string TokenUnreadable = "token_unreadable";
    public const string AdminLeft = "admin_left";
    public const string AccessLost = "access_lost";
}

internal sealed class ConnectionTokens(
    BrandsDbContext db,
    ITokenCipher cipher,
    GoogleOAuth oauth,
    AccessTokenCache cache,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    IServiceScopeFactory scopes,
    ILogger<ConnectionTokens> logger)
{
    public async Task SealAsync(Connection connection, string refreshToken, OAuthClient? client, CancellationToken cancellationToken)
    {
        var sealedToken = await cipher.SealAsync(refreshToken, Context(connection), cancellationToken);
        connection.RefreshToken = sealedToken.Ciphertext;
        connection.KeyVersion = sealedToken.KeyVersion;
        connection.ClientId = client?.Id;
        connection.ClientSecret = client is null
            ? null
            : (await cipher.SealAsync(client.Secret, ClientContext(connection), cancellationToken)).Ciphertext;
    }

    public async Task<OAuthClient?> OpenClientAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection is not { ClientId: { } clientId, ClientSecret: { } clientSecret })
        {
            return null;
        }

        try
        {
            return new OAuthClient(clientId, await cipher.OpenAsync(clientSecret, ClientContext(connection), cancellationToken));
        }
        catch (CryptographicException)
        {
            return null;
        }
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
        if (connection.ClientId is not null)
        {
            return null;
        }

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

        var refreshToken = await OpenAsync(connection, cancellationToken);
        var client = await OpenClientAsync(connection, cancellationToken);
        if (refreshToken is null || (connection.ClientId is not null && client is null))
        {
            await LoseOnItsOwnAsync(connection, ReconnectReasons.TokenUnreadable, cancellationToken);
            return null;
        }

        if (await oauth.RefreshAsync(refreshToken, client, cancellationToken) is not { } grant)
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
        connection.ClientSecret = null;
        connection.KeyVersion = null;
        connection.ReconnectReason = reason;
        cache.Forget(connection.Id);
        await db.SaveChangesAsync(cancellationToken);
        await RecordLossAsync(connection, before, reason, cancellationToken);
    }

    private async Task RecordLossAsync(Connection connection, object before, string reason, CancellationToken cancellationToken)
    {
        KairoTelemetry.Event(logger, "connection.expired", connection.BrandId);
        await audit.RecordAsync(
            new AuditRecord("connection.needs_reconnect", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
            cancellationToken);
        await live.PublishAsync(ConnectionEvents.Changed(connection.Product), cancellationToken);
        await notifications.NotifyAsync(ConnectionNotices.NeedsReconnect(connection.Product, reason), cancellationToken);
    }

    public async Task LoseOnItsOwnAsync(Connection connection, string reason, CancellationToken cancellationToken)
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
                var before = ConnectionLifecycle.Snapshot(current);
                var lost = await own.Connections
                    .Where(row => row.Id == current.Id && row.ConnectedAt == current.ConnectedAt && row.RefreshToken != null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.State, ConnectionState.NeedsReconnect)
                        .SetProperty(row => row.RefreshToken, (byte[]?)null)
                        .SetProperty(row => row.ClientSecret, (byte[]?)null)
                        .SetProperty(row => row.KeyVersion, (string?)null)
                        .SetProperty(row => row.ReconnectReason, reason), cancellationToken);
                if (lost == 1)
                {
                    await own.Entry(current).ReloadAsync(cancellationToken);
                    await scope.ServiceProvider.GetRequiredService<ConnectionTokens>().RecordLossAsync(current, before, reason, cancellationToken);
                }
            }
        }

        await db.Entry(connection).ReloadAsync(cancellationToken);
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";

    private static string ClientContext(Connection connection) => $"{Context(connection)}:client";
}
