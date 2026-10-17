using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal enum ConnectionMethod
{
    Google,
    ClientCredentials,
}

internal static class ConnectionLifecycle
{
    public static readonly TimeSpan ChoosingTimeout = TimeSpan.FromHours(1);

    public static ConnectionState StateOf(Connection? connection, DateTimeOffset now) => connection switch
    {
        null => ConnectionState.NotConnected,
        { State: ConnectionState.ChoosingAccount } choosing when choosing.ConnectedAt + ChoosingTimeout < now => ConnectionState.NotConnected,
        { } existing => existing.State,
    };

    public static Task<Connection?> FindAsync(BrandsDbContext db, ConnectedProduct product, CancellationToken cancellationToken) =>
        db.Connections.SingleOrDefaultAsync(connection => connection.Product == product, cancellationToken);

    public static ConnectionMethod MethodOf(Connection connection) =>
        connection.ClientId is null ? ConnectionMethod.Google : ConnectionMethod.ClientCredentials;

    public static object Snapshot(Connection connection) => new
    {
        Product = Products.Get(connection.Product).Slug,
        State = connection.State.ToString(),
        Method = MethodOf(connection).ToString(),
        connection.ExternalId,
        connection.DisplayName,
        connection.ManagerId,
        connection.ReconnectReason,
    };
}
