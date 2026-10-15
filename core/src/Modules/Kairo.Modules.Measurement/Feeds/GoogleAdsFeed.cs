using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class GoogleAdsFeed(GoogleAdsReports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.GoogleAds;

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    public IReadOnlyList<TableSchema> Tables { get; } =
    [
        TableSchemas.AdsCampaignDaily,
        TableSchemas.AdsAdGroupDaily,
        TableSchemas.AdsAdDaily,
        TableSchemas.AdsRegionDaily,
        TableSchemas.AdsCityDaily,
    ];

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, FeedHistory history, CancellationToken cancellationToken)
    {
        var account = await reports.DescribeAsync(access, cancellationToken);
        var today = FeedClock.Today(time, account.TimeZone);
        var window = history.WindowFor(this, today);
        var reads = new Dictionary<string, Func<FeedWindow, Task<List<JsonObject>>>>
        {
            [TableSchemas.AdsCampaignDaily.Name] = span => reports.CampaignsAsync(access, account, span, cancellationToken),
            [TableSchemas.AdsAdGroupDaily.Name] = span => reports.AdGroupsAsync(access, account, span, cancellationToken),
            [TableSchemas.AdsAdDaily.Name] = span => reports.AdsAsync(access, account, span, cancellationToken),
            [TableSchemas.AdsRegionDaily.Name] = span => reports.RegionsAsync(access, account, span, cancellationToken),
            [TableSchemas.AdsCityDaily.Name] = span => reports.CitiesAsync(access, account, span, cancellationToken),
        };
        var rows = 0;
        foreach (var month in window.Months())
        {
            foreach (var table in Tables)
            {
                if (history.Span(table, month, today) is { } span)
                {
                    rows += await tables.ReplaceDaysAsync(brandId, table, span.Days(), await reads[table.Name](span), cancellationToken);
                }
            }
        }

        return new FeedResult(window, rows);
    }
}
