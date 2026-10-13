using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record FeedStatusView(
    string Feed,
    string State,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? LastSuccessAt,
    DateOnly? CoveredThrough,
    string? LastError);

internal sealed record FeedsView(string Dataset, FeedStatusView[] Feeds);

internal static class FeedState
{
    public static string Of(ConnectionState connection, bool isSetUp, FeedRun? latest, DateTimeOffset now)
    {
        if (!isSetUp || connection == ConnectionState.NotConnected)
        {
            return FeedStates.NotSetUp;
        }

        if (connection is ConnectionState.NeedsReconnect or ConnectionState.ChoosingAccount)
        {
            return FeedStates.NeedsConnection;
        }

        if (latest is null || latest.Status == RunStatuses.Skipped)
        {
            return FeedStates.Waiting;
        }

        if (latest.Status == RunStatuses.Running)
        {
            return latest.StartedAt > now - BrandFeeds.RunningFor ? FeedStates.Running : FeedStates.Failing;
        }

        return latest.Status == RunStatuses.Failed ? FeedStates.Failing : FeedStates.Ok;
    }
}

internal static class FeedEndpoints
{
    private static readonly (string Feed, ConnectedProduct Product)[] Feeds =
    [
        (FeedNames.GoogleAds, ConnectedProduct.GoogleAds),
        (FeedNames.Ga4, ConnectedProduct.Ga4),
        (FeedNames.SalesTable, ConnectedProduct.BigQuery),
    ];

    public static async Task<Ok<FeedsView>> StatusAsync(
        MeasurementDbContext db,
        IConnections connections,
        CurrentMember member,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hasSource = await db.SalesSources.AnyAsync(cancellationToken);
        var views = new List<FeedStatusView>();
        foreach (var (feed, product) in Feeds)
        {
            var connection = (await connections.GetStatusAsync(product, cancellationToken)).State;
            var latest = await LatestAsync(db, feed, null, cancellationToken);
            var success = await LatestAsync(db, feed, RunStatuses.Succeeded, cancellationToken);
            var state = FeedState.Of(connection, feed != FeedNames.SalesTable || hasSource, latest, now);
            var failure = state == FeedStates.Failing ? await LatestAsync(db, feed, RunStatuses.Failed, cancellationToken) : null;
            views.Add(new FeedStatusView(feed, state, latest?.StartedAt, success?.FinishedAt, success?.ToDate, failure?.Error));
        }

        return TypedResults.Ok(new FeedsView(AnalyticsOptions.DatasetFor(member.BrandId), views.ToArray()));
    }

    private static Task<FeedRun?> LatestAsync(MeasurementDbContext db, string feed, string? status, CancellationToken cancellationToken) =>
        db.FeedRuns
            .Where(run => run.Feed == feed && (status == null || run.Status == status))
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
