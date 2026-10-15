using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Modules.Opportunities.Scoring;

namespace Kairo.Modules.Opportunities.Refresh;

internal sealed record KeywordDemand(string Text, IReadOnlyList<MonthlySearches> Months, BidMicros? Bids = null);

internal sealed record BidMicros(long Low, long High);

internal sealed record GeoTarget(string Id, string Name, string Type);

internal sealed record Market(GeoTarget Country, IReadOnlyList<GeoTarget> Regions);

internal sealed class KeywordPlanner(IHttpClientFactory http, OpportunityOptions options, TimeProvider time)
{
    public const int SeedsPerCall = 20;
    public const int IdeasPerCall = 1000;
    public const int HistoryMonths = 24;

    private static readonly string[] MonthNames =
    [
        "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE",
        "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER",
    ];

    public async Task<Market> MarketAsync(ConnectionAccess access, CancellationToken cancellationToken)
    {
        var query = "SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.target_type FROM geo_target_constant "
            + $"WHERE geo_target_constant.country_code = '{options.CountryCode}' "
            + "AND geo_target_constant.target_type IN ('Country', 'State') AND geo_target_constant.status = 'ENABLED'";
        var answer = await CallAsync(access, "googleAds:search", new { query }, cancellationToken);
        var targets = Results(answer)
            .Select(row => row.TryGetProperty("geoTargetConstant", out var geo) ? geo : default)
            .Where(geo => geo.ValueKind == JsonValueKind.Object && AdsHttp.Text(geo, "id") is not null)
            .Select(geo => new GeoTarget(AdsHttp.Text(geo, "id")!, AdsHttp.Text(geo, "name") ?? "", AdsHttp.Text(geo, "targetType") ?? ""))
            .ToList();
        var country = targets.FirstOrDefault(target => target.Type == "Country")
            ?? throw new InvalidOperationException($"Google Ads has no country target for {options.CountryCode}.");
        return new Market(country, targets.Where(target => target.Type == "State").OrderBy(target => target.Name, StringComparer.Ordinal).ToList());
    }

    public async Task<IReadOnlyList<KeywordDemand>> IdeasAsync(
        ConnectionAccess access,
        GeoTarget country,
        IReadOnlyList<string> seeds,
        CancellationToken cancellationToken)
    {
        var ideas = new Dictionary<string, KeywordDemand>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in seeds.Chunk(SeedsPerCall))
        {
            var answer = await CallAsync(
                access,
                ":generateKeywordIdeas",
                new
                {
                    language = $"languageConstants/{options.Language}",
                    geoTargetConstants = new[] { $"geoTargetConstants/{country.Id}" },
                    keywordPlanNetwork = "GOOGLE_SEARCH",
                    includeAdultKeywords = false,
                    pageSize = IdeasPerCall,
                    historicalMetricsOptions = History(),
                    keywordSeed = new { keywords = batch },
                },
                cancellationToken);
            foreach (var demand in Demands(answer, "keywordIdeaMetrics"))
            {
                ideas.TryAdd(demand.Text, demand);
            }
        }

        return ideas.Values.ToList();
    }

    public async Task<IReadOnlyList<KeywordDemand>> HistoryAsync(
        ConnectionAccess access,
        GeoTarget region,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var answer = await CallAsync(
            access,
            ":generateKeywordHistoricalMetrics",
            new
            {
                keywords,
                language = $"languageConstants/{options.Language}",
                geoTargetConstants = new[] { $"geoTargetConstants/{region.Id}" },
                keywordPlanNetwork = "GOOGLE_SEARCH",
                includeAdultKeywords = false,
                historicalMetricsOptions = History(),
            },
            cancellationToken);
        return Demands(answer, "keywordMetrics").ToList();
    }

    private object History()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var end = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var start = end.AddMonths(1 - HistoryMonths);
        return new
        {
            yearMonthRange = new
            {
                start = new { year = start.Year, month = MonthNames[start.Month - 1] },
                end = new { year = end.Year, month = MonthNames[end.Month - 1] },
            },
        };
    }

    private async Task<JsonElement> CallAsync(ConnectionAccess access, string method, object body, CancellationToken cancellationToken)
    {
        var separator = method.StartsWith(':') ? "" : "/";
        return await http.CreateClient(AdsHttp.ClientName).PostAsync(
            $"https://googleads.googleapis.com/{options.AdsApiVersion}/customers/{access.ExternalId}{separator}{method}",
            access.AccessToken,
            access.ManagerId,
            body,
            cancellationToken);
    }

    private static IEnumerable<JsonElement> Results(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
            : [];

    private static IEnumerable<KeywordDemand> Demands(JsonElement answer, string metricsName)
    {
        foreach (var result in Results(answer))
        {
            if (AdsHttp.Text(result, "text") is not { Length: > 0 } text
                || !result.TryGetProperty(metricsName, out var metrics)
                || !metrics.TryGetProperty("monthlySearchVolumes", out var volumes)
                || volumes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var months = volumes.EnumerateArray()
                .Select(volume => (Year: (int)AdsHttp.Number(volume, "year"), Month: MonthOf(volume), Searches: AdsHttp.Number(volume, "monthlySearches")))
                .Where(volume => volume.Year > 0 && volume.Month > 0)
                .Select(volume => new MonthlySearches(volume.Year, volume.Month, volume.Searches))
                .ToList();
            if (months.Count > 0)
            {
                yield return new KeywordDemand(text, months, BidsOf(metrics));
            }
        }
    }

    private static BidMicros? BidsOf(JsonElement metrics)
    {
        var low = AdsHttp.Number(metrics, "lowTopOfPageBidMicros");
        var high = AdsHttp.Number(metrics, "highTopOfPageBidMicros");
        return low > 0 && high >= low ? new BidMicros(low, high) : null;
    }

    private static int MonthOf(JsonElement volume)
    {
        var month = AdsHttp.Text(volume, "month");
        if (month is null)
        {
            return 0;
        }

        var index = Array.IndexOf(MonthNames, month.ToUpperInvariant());
        if (index >= 0)
        {
            return index + 1;
        }

        return int.TryParse(month, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is >= 2 and <= 13 ? number - 1 : 0;
    }
}
