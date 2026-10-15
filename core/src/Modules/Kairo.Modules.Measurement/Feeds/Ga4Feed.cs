using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class Ga4Feed(Ga4Reports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.Ga4;

    public ConnectedProduct Product => ConnectedProduct.Ga4;

    public IReadOnlyList<TableSchema> Tables { get; } = [TableSchemas.Ga4RegionDaily, TableSchemas.Ga4CityDaily];

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, FeedHistory history, CancellationToken cancellationToken)
    {
        var property = await reports.DescribeAsync(access, cancellationToken);
        var today = FeedClock.Today(time, property.TimeZone);
        var window = history.WindowFor(this, today);
        var rows = 0;
        foreach (var month in window.Months())
        {
            foreach (var table in Tables)
            {
                if (history.Span(table, month, today) is { } span)
                {
                    var cities = table == TableSchemas.Ga4CityDaily;
                    rows += await tables.ReplaceDaysAsync(
                        brandId,
                        table,
                        span.Days(),
                        await reports.DailyAsync(access, property, span, cities, cancellationToken),
                        cancellationToken);
                }
            }
        }

        return new FeedResult(window, rows);
    }
}
