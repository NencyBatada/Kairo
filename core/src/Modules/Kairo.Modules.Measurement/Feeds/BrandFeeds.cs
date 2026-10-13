using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class BrandFeeds(
    IEnumerable<IFeed> feeds,
    IConnections connections,
    MeasurementDbContext db,
    TimeProvider time,
    ILogger<BrandFeeds> logger)
{
    public const int MaxErrorLength = 1000;

    public static readonly TimeSpan RunningFor = TimeSpan.FromHours(2);

    public async Task<IReadOnlyList<FeedOutcome>> RunAsync(Guid brandId, CancellationToken cancellationToken)
    {
        var outcomes = new List<FeedOutcome>();
        foreach (var feed in feeds)
        {
            if (await RunAsync(brandId, feed, cancellationToken) is { } outcome)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }

    private async Task<FeedOutcome?> RunAsync(Guid brandId, IFeed feed, CancellationToken cancellationToken)
    {
        if (!await feed.IsSetUpAsync(cancellationToken))
        {
            return null;
        }

        var status = await connections.GetStatusAsync(feed.Product, cancellationToken);
        var now = time.GetUtcNow();
        var run = new FeedRun { Id = Guid.NewGuid(), Feed = feed.Name, ExternalId = status.ExternalId, StartedAt = now };
        db.FeedRuns.Add(run);
        if (status.State != ConnectionState.Connected)
        {
            var reason = status.State == ConnectionState.NeedsReconnect ? SkipReasons.NeedsReconnect : SkipReasons.NotConnected;
            return await FinishAsync(brandId, run, RunStatuses.Skipped, reason, cancellationToken);
        }

        var since = now - RunningFor;
        if (await db.FeedRuns.AnyAsync(
                existing => existing.Feed == feed.Name && existing.Status == RunStatuses.Running && existing.StartedAt > since,
                cancellationToken))
        {
            return await FinishAsync(brandId, run, RunStatuses.Skipped, SkipReasons.AlreadyRunning, cancellationToken);
        }

        var target = feed.TargetOf(status);
        run.ExternalId = target;
        var firstRun = !await db.FeedRuns.AnyAsync(
            existing => existing.Feed == feed.Name && existing.ExternalId == target && existing.Status == RunStatuses.Succeeded,
            cancellationToken);
        run.Status = RunStatuses.Running;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var access = await connections.GetAccessAsync(feed.Product, cancellationToken);
            var result = await feed.RunAsync(brandId, access, firstRun, cancellationToken);
            run.FromDate = result.Window.From;
            run.ToDate = result.Window.To;
            run.Rows = result.Rows;
            return await FinishAsync(brandId, run, RunStatuses.Succeeded, null, cancellationToken);
        }
        catch (ConnectionUnavailableException unavailable)
        {
            var reason = unavailable.Reason == SkipReasons.NotConnected ? SkipReasons.NotConnected : SkipReasons.NeedsReconnect;
            return await FinishAsync(brandId, run, RunStatuses.Skipped, reason, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The {Feed} feed failed for brand {BrandId}.", feed.Name, brandId);
            run.Error = exception.Message.Length > MaxErrorLength ? exception.Message[..MaxErrorLength] : exception.Message;
            return await FinishAsync(brandId, run, RunStatuses.Failed, null, cancellationToken);
        }
    }

    private async Task<FeedOutcome> FinishAsync(Guid brandId, FeedRun run, string status, string? reason, CancellationToken cancellationToken)
    {
        run.Status = status;
        run.Reason = reason;
        run.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new FeedOutcome(brandId, run.Feed, status, run.Rows);
    }
}
