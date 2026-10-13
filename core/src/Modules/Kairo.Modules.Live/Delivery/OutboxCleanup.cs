using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class OutboxCleanup(LiveDbContext db, KairoDatabase database, TimeProvider time)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - LiveTimings.Retention;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            await database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    DECLARE @lock int;
                    EXEC @lock = sys.sp_getapplock @Resource = N'live.outbox.cleanup', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
                    IF @lock >= 0
                        DELETE FROM [live].[Outbox] WITH (READCOMMITTEDLOCK, READPAST) WHERE [CreatedAt] < {cutoff};
                    """,
                    cancellationToken);
                await database.CommitAsync(cancellationToken);
            }
            catch
            {
                await database.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
