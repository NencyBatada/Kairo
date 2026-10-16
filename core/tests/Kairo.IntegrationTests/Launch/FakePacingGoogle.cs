using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kairo.IntegrationTests.Launch;

public sealed class FakePacingGoogle(FakeLaunchGoogle launch)
{
    public Dictionary<string, string> Campaigns { get; } = [];
    public Dictionary<(string CampaignType, DateOnly Date), (decimal Spend, long Clicks)> Days { get; } = [];
    public string[] Reasons { get; set; } = [];
    public int Ads { get; set; } = 4;
    public int PausedAds { get; set; }
    public decimal? RankLost { get; set; }
    public List<string> Queries { get; } = [];

    public HttpResponseMessage? Respond(Uri uri, string body)
    {
        if (uri.Host != "googleads.googleapis.com" || !uri.AbsolutePath.EndsWith("/googleAds:search", StringComparison.Ordinal)) return launch.Respond(uri, body);
        var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;
        if (query.Contains("metrics.cost_micros", StringComparison.Ordinal))
        {
            Queries.Add(query);
            var range = Regex.Match(query, @"BETWEEN '([0-9-]+)' AND '([0-9-]+)'");
            var first = DateOnly.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
            var last = DateOnly.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
            return Json(Days.Where(pair => pair.Key.Date >= first && pair.Key.Date <= last && Campaigns.ContainsKey(pair.Key.CampaignType)).Select(pair => new
            {
                campaign = new { resourceName = Campaigns[pair.Key.CampaignType] },
                segments = new { date = pair.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                metrics = new
                {
                    costMicros = decimal.ToInt64(pair.Value.Spend * 1_000_000m).ToString(CultureInfo.InvariantCulture),
                    conversions = 2.0,
                    conversionsValue = 3000.0,
                    impressions = "1000",
                    clicks = pair.Value.Clicks.ToString(CultureInfo.InvariantCulture),
                },
            }));
        }
        if (query.Contains("campaign.primary_status_reasons", StringComparison.Ordinal) && !query.Contains("metrics.", StringComparison.Ordinal))
        {
            Queries.Add(query);
            return Json(Campaigns.Values.Select(name => new { campaign = new { resourceName = name, primaryStatus = "ELIGIBLE", primaryStatusReasons = Reasons } }));
        }
        if (query.Contains("search_rank_lost_impression_share", StringComparison.Ordinal))
        {
            Queries.Add(query);
            return Json(RankLost is { } lost ? Campaigns.Values.Select(name => (object)new { campaign = new { resourceName = name }, metrics = new { searchRankLostImpressionShare = lost } }) : []);
        }
        if (query.StartsWith("SELECT campaign.resource_name, ad_group_ad.", StringComparison.Ordinal))
        {
            Queries.Add(query);
            return Json(Campaigns.Values.SelectMany(name => Enumerable.Range(0, Ads).Select(index => new
            {
                campaign = new { resourceName = name },
                adGroupAd = new { resourceName = $"{name}/ads/{index}", status = index < PausedAds ? "PAUSED" : "ENABLED", policySummary = new { approvalStatus = "APPROVED" } },
            })));
        }
        return launch.Respond(uri, body);
    }

    private static HttpResponseMessage Json<T>(IEnumerable<T> rows) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { results = rows.ToArray() }) };
}
