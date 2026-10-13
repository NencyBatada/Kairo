using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal static class AdsQueries
{
    public const string Metrics = "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value";

    public const string Customer = "SELECT customer.id, customer.time_zone, customer.currency_code FROM customer";

    public static string Campaigns(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, campaign.name, campaign.advertising_channel_type, campaign.status, {Metrics} FROM campaign WHERE {During(window)}";

    public static string AdGroups(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, ad_group.id, ad_group.name, {Metrics} FROM ad_group WHERE {During(window)}";

    public static string Ads(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, ad_group.id, ad_group_ad.ad.id, ad_group_ad.ad.type, {Metrics} FROM ad_group_ad WHERE {During(window)}";

    public static string Regions(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, {GoogleAdsReports.RegionField}, {Metrics} FROM geographic_view "
        + $"WHERE {During(window)} AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'";

    public static string GeoTargets(IEnumerable<string> resourceNames) =>
        "SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.country_code FROM geo_target_constant "
        + $"WHERE geo_target_constant.resource_name IN ({string.Join(", ", resourceNames.Select(name => $"'{name}'"))})";

    private static string During(FeedWindow window) =>
        $"segments.date BETWEEN '{FeedClock.Iso(window.From)}' AND '{FeedClock.Iso(window.To)}'";
}

internal sealed class GoogleAdsReports(IHttpClientFactory http, AnalyticsOptions options)
{
    public const string RegionField = "segments.geo_target_state";
    public const string RegionJson = "geoTargetState";
    public const int GeoBatch = 500;

    public async Task<AdsAccount> DescribeAsync(ConnectionAccess access, CancellationToken cancellationToken)
    {
        var customer = (await SearchAsync(access, AdsQueries.Customer, cancellationToken)).Single().GetProperty("customer");
        return new AdsAccount(
            access.ExternalId,
            AdsRows.Text(customer, "timeZone") ?? FeedClock.IndiaTimeZone,
            AdsRows.Text(customer, "currencyCode") ?? "");
    }

    public async Task<List<JsonObject>> CampaignsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.Campaigns(window), cancellationToken)).Select(row => AdsRows.Campaign(row, account)).ToList();

    public async Task<List<JsonObject>> AdGroupsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.AdGroups(window), cancellationToken)).Select(row => AdsRows.AdGroup(row, account)).ToList();

    public async Task<List<JsonObject>> AdsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.Ads(window), cancellationToken)).Select(row => AdsRows.Ad(row, account)).ToList();

    public async Task<List<JsonObject>> RegionsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken)
    {
        var rows = await SearchAsync(access, AdsQueries.Regions(window), cancellationToken);
        var names = new Dictionary<string, GeoTarget>();
        foreach (var batch in rows.Select(AdsRows.RegionOf).OfType<string>().Distinct().Chunk(GeoBatch))
        {
            foreach (var row in await SearchAsync(access, AdsQueries.GeoTargets(batch), cancellationToken))
            {
                var geo = row.GetProperty("geoTargetConstant");
                if (AdsRows.Text(geo, "id") is { } id)
                {
                    names[$"geoTargetConstants/{id}"] = new GeoTarget(id, AdsRows.Text(geo, "name"), AdsRows.Text(geo, "countryCode"));
                }
            }
        }

        return rows.Select(row => AdsRows.Region(row, account, names)).ToList();
    }

    private async Task<IReadOnlyList<JsonElement>> SearchAsync(ConnectionAccess access, string query, CancellationToken cancellationToken)
    {
        (string Name, string Value)[] headers = access.ManagerId is { } manager ? [("login-customer-id", manager)] : [];
        var batches = await http.CreateClient(MeasurementHttp.ClientName).CallAsync(
            HttpMethod.Post,
            $"https://googleads.googleapis.com/{options.AdsApiVersion}/customers/{access.ExternalId}/googleAds:searchStream",
            access.AccessToken,
            new { query },
            cancellationToken,
            headers);
        var results = new List<JsonElement>();
        if (batches.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var batch in batches.EnumerateArray())
        {
            if (batch.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                results.AddRange(list.EnumerateArray());
            }
        }

        return results;
    }
}
