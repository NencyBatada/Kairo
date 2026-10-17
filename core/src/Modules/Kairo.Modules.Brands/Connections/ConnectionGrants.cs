using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal sealed record NewGrant(
    Guid BrandId,
    ProductInfo Product,
    string Subject,
    string RefreshToken,
    OAuthClient? Client,
    Guid UserId,
    string UserName);

internal sealed class ConnectionGrants(
    KairoDatabase database,
    BrandsDbContext db,
    ConnectionTokens tokens,
    GoogleRevocations revocations,
    IAuditLog audit,
    ILiveUpdates live,
    TimeProvider time)
{
    public async Task<Connection> StoreAsync(NewGrant grant, CancellationToken cancellationToken)
    {
        var ownsTransaction = !database.HasTransaction;
        if (ownsTransaction)
        {
            await database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var connection = await ConnectionLifecycle.FindAsync(db, grant.Product.Product, cancellationToken);
            var before = connection is null ? null : ConnectionLifecycle.Snapshot(connection);
            if (connection is not null && await tokens.OpenForRevocationAsync(connection, cancellationToken) is { } oldToken)
            {
                await revocations.RevokeAfterCommitAsync(connection.GoogleSubject, oldToken, cancellationToken);
            }

            if (connection is null)
            {
                connection = new Connection { BrandId = grant.BrandId, Product = grant.Product.Product };
                db.Connections.Add(connection);
            }

            connection.State = ConnectionState.ChoosingAccount;
            connection.ExternalId = null;
            connection.DisplayName = null;
            connection.ManagerId = null;
            connection.Scopes = string.Join(' ', grant.Product.Scopes);
            connection.GoogleSubject = grant.Subject;
            connection.ConnectedBy = grant.UserId;
            connection.ConnectedByName = grant.UserName;
            connection.ConnectedAt = time.GetUtcNow();
            connection.CheckedAt = null;
            connection.ReconnectReason = null;
            await tokens.SealAsync(connection, grant.RefreshToken, grant.Client, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord("connection.connected", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
                cancellationToken);
            await live.PublishAsync(ConnectionEvents.Changed(grant.Product.Product), cancellationToken);
            if (ownsTransaction)
            {
                await database.CommitAsync(cancellationToken);
            }

            return connection;
        }
        catch
        {
            if (ownsTransaction)
            {
                await database.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
