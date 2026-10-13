using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionEndpoints
{
    public static async Task<Ok<ConnectionView[]>> ListAsync(BrandsDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var rows = await db.Connections.ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        return TypedResults.Ok(Products.All
            .Select(product => ConnectionView.Of(product, rows.SingleOrDefault(row => row.Product == product.Product), now))
            .ToArray());
    }
}

internal sealed record ConnectionView(
    string Product,
    string Name,
    ConnectionState State,
    string? ExternalId,
    string? DisplayName,
    Guid? ConnectedBy,
    string? ConnectedByName,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? CheckedAt,
    string? ReconnectReason)
{
    public static ConnectionView Of(ProductInfo product, Connection? connection, DateTimeOffset now)
    {
        var state = ConnectionLifecycle.StateOf(connection, now);
        return connection is null || state == ConnectionState.NotConnected
            ? new(product.Slug, product.Name, ConnectionState.NotConnected, null, null, null, null, null, null, null)
            : new(product.Slug, product.Name, state, connection.ExternalId, connection.DisplayName, connection.ConnectedBy,
                connection.ConnectedByName, connection.ConnectedAt, connection.CheckedAt, connection.ReconnectReason);
    }
}
