using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class GoogleAdsFeed(GoogleAdsReports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.GoogleAds;

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var account = await reports.DescribeAsync(access, cancellationToken);
        var window = FeedWindow.For(FeedClock.Today(time, account.TimeZone), firstRun);
        var rows = 0;
        foreach (var month in window.Months())
        {
            var days = month.Days();
            rows += await tables.ReplaceDaysAsync(
                brandId, TableSchemas.AdsCampaignDaily, days, await reports.CampaignsAsync(access, account, month, cancellationToken), cancellationToken);
            rows += await tables.ReplaceDaysAsync(
                brandId, TableSchemas.AdsAdGroupDaily, days, await reports.AdGroupsAsync(access, account, month, cancellationToken), cancellationToken);
            rows += await tables.ReplaceDaysAsync(
                brandId, TableSchemas.AdsAdDaily, days, await reports.AdsAsync(access, account, month, cancellationToken), cancellationToken);
            rows += await tables.ReplaceDaysAsync(
                brandId, TableSchemas.AdsRegionDaily, days, await reports.RegionsAsync(access, account, month, cancellationToken), cancellationToken);
        }

        return new FeedResult(window, rows);
    }
}
