using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal sealed class BrandConnections(
    BrandsDbContext db,
    ConnectionTokens tokens,
    GoogleRevocations revocations,
    TimeProvider time) : IConnections
{
    public async Task<ConnectionStatus> GetStatusAsync(ConnectedProduct product, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, product, cancellationToken);
        var state = ConnectionLifecycle.StateOf(connection, time.GetUtcNow());
        return state == ConnectionState.NotConnected
            ? new ConnectionStatus(product, state, null, null)
            : new ConnectionStatus(product, state, connection!.ExternalId, connection.DisplayName);
    }

    public async Task<ConnectionAccess> GetAccessAsync(ConnectedProduct product, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, product, cancellationToken);
        if (connection is not { State: ConnectionState.Connected, ExternalId: { } externalId })
        {
            throw new ConnectionUnavailableException(
                product,
                connection?.State == ConnectionState.NeedsReconnect ? connection.ReconnectReason ?? "needs_reconnect" : "not_connected");
        }

        var token = await tokens.GetAsync(connection, fresh: false, cancellationToken)
            ?? throw new ConnectionUnavailableException(product, connection.ReconnectReason ?? ReconnectReasons.TokenRejected);
        return new ConnectionAccess(externalId, token.Value, token.ExpiresAt, connection.ManagerId);
    }

    public async Task ReleaseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var theirs = await db.Connections
            .Where(connection => connection.ConnectedBy == userId && connection.RefreshToken != null)
            .ToListAsync(cancellationToken);

        var revoke = new List<(string Subject, string Token)>();
        foreach (var connection in theirs)
        {
            if (await tokens.OpenForRevocationAsync(connection, cancellationToken) is { } refreshToken)
            {
                revoke.Add((connection.GoogleSubject, refreshToken));
            }

            await tokens.LoseAsync(connection, ReconnectReasons.AdminLeft, cancellationToken);
        }

        foreach (var (subject, refreshToken) in revoke)
        {
            await revocations.RevokeIfUnusedAsync(subject, refreshToken, cancellationToken);
        }
    }
}
