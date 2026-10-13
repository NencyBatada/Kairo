using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Connections.Accounts;
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

    public static async Task<Results<Ok<CheckResult>, NotFound, ProblemHttpResult>> CheckAsync(
        string product,
        BrandsDbContext db,
        ConnectionTokens tokens,
        ProductCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var connection = await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken);
        if (connection is null || ConnectionLifecycle.StateOf(connection, now) is not (ConnectionState.Connected or ConnectionState.NeedsReconnect))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: AccountEndpoints.ConnectFirst);
        }

        if (connection.State == ConnectionState.NeedsReconnect
            || await tokens.GetAsync(connection, fresh: true, cancellationToken) is not { } token)
        {
            return TypedResults.Ok(new CheckResult(ConnectionView.Of(info, connection, now), AccountEndpoints.AccessLost));
        }

        string? problem;
        try
        {
            problem = await catalog.For(info.Product).TestAsync(
                token.Value,
                new ProductAccount(connection.ExternalId!, connection.DisplayName ?? connection.ExternalId!, connection.ManagerId),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            problem = AccountEndpoints.GoogleDidNotAnswer;
        }

        connection.CheckedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new CheckResult(ConnectionView.Of(info, connection, now), problem));
    }

    public static async Task<Results<NoContent, NotFound>> DisconnectAsync(
        string product,
        BrandsDbContext db,
        ConnectionTokens tokens,
        AccessTokenCache cache,
        GoogleRevocations revocations,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        if (await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken) is not { } connection)
        {
            return TypedResults.NoContent();
        }

        var refreshToken = await tokens.OpenForRevocationAsync(connection, cancellationToken);
        var before = ConnectionLifecycle.Snapshot(connection);
        db.Connections.Remove(connection);
        cache.Forget(connection.Id);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("connection.disconnected", "connection", connection.Id.ToString(), before), cancellationToken);
        if (refreshToken is not null)
        {
            await revocations.RevokeIfUnusedAsync(connection.GoogleSubject, refreshToken, cancellationToken);
        }

        return TypedResults.NoContent();
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

internal sealed record CheckResult(ConnectionView Connection, string? Problem);
