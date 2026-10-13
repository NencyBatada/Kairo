using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class Ga4Feed(Ga4Reports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.Ga4;

    public ConnectedProduct Product => ConnectedProduct.Ga4;

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var property = await reports.DescribeAsync(access, cancellationToken);
        var window = FeedWindow.For(FeedClock.Today(time, property.TimeZone), firstRun);
        var rows = await tables.ReplaceDaysAsync(
            brandId,
            TableSchemas.Ga4RegionDaily,
            window.Days(),
            await reports.DailyAsync(access, property, window, cancellationToken),
            cancellationToken);
        return new FeedResult(window, rows);
    }
}
