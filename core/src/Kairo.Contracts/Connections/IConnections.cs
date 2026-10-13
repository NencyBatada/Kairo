namespace Kairo.Contracts.Connections;

public enum ConnectedProduct
{
    GoogleAds,
    YouTube,
    MerchantCenter,
    Ga4,
    BigQuery,
}

public enum ConnectionState
{
    NotConnected,
    ChoosingAccount,
    Connected,
    NeedsReconnect,
}

public interface IConnections
{
    Task<ConnectionStatus> GetStatusAsync(ConnectedProduct product, CancellationToken cancellationToken);

    Task<ConnectionAccess> GetAccessAsync(ConnectedProduct product, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record ConnectionStatus(ConnectedProduct Product, ConnectionState State, string? ExternalId, string? DisplayName);

public sealed record ConnectionAccess(string ExternalId, string AccessToken, DateTimeOffset ExpiresAt, string? ManagerId);

public sealed class ConnectionUnavailableException(ConnectedProduct product, string reason)
    : Exception($"The {product} connection is unavailable: {reason}.")
{
    public ConnectedProduct Product { get; } = product;

    public string Reason { get; } = reason;
}
