using Kairo.Modules.BrandKit.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed class ClaimedRun
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }
}

internal sealed class DraftDispatcher(
    BrandKitDbContext db,
    KairoDatabase database,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<DraftDispatcher> logger)
{
    public const int BatchSize = 2;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var lease = time.GetUtcNow() + Lease;
        List<ClaimedRun> claimed;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            claimed = await ClaimAsync(lease, cancellationToken);
        }

        await Task.WhenAll(claimed.Select(run => RunAsync(run, lease, cancellationToken)));
        return claimed.Count;
    }

    private Task<List<ClaimedRun>> ClaimAsync(DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Database.SqlQuery<ClaimedRun>($"""
            UPDATE TOP (2) brand_kit.DraftRuns WITH (READPAST, UPDLOCK, ROWLOCK)
            SET ClaimedUntil = {lease}, Attempts = Attempts + 1, Status = N'Running', StartedAt = COALESCE(StartedAt, {now})
            OUTPUT inserted.Id, inserted.BrandId
            WHERE Status IN (N'Pending', N'Running') AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
            """).ToListAsync(cancellationToken);
    }

    private async Task RunAsync(ClaimedRun claimed, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(claimed.BrandId, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<DraftRunner>().RunAsync(claimed.Id, lease, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Drafting run {RunId} stopped unexpectedly. It runs again when its lease ends.", claimed.Id);
        }
    }
}
