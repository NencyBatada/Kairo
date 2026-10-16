using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kairo.IntegrationTests.Launch;

namespace Kairo.IntegrationTests.Care;

public sealed record CareDay(DateOnly Date, decimal Cost, decimal Conversions, decimal Value);

public sealed record CareSuggestion(string Id, string Type, string CampaignType, decimal ExtraConversions = 5, long ExtraClicks = 40, decimal ExtraCost = 1000);

public sealed partial class FakeCareGoogle(FakeLaunchGoogle launch)
{
    public Dictionary<string, List<CareDay>> Days { get; } = [];
    public List<CareSuggestion> Suggestions { get; } = [];
    public List<JsonElement> Mutations { get; } = [];
    public List<string> Applied { get; } = [];
    public bool FailWrites { get; set; }
    public int TransientReads { get; set; }
    public int SuggestionReads { get; private set; }

    public HttpResponseMessage? Respond(Uri uri, string body) => Handle(uri, body) ?? launch.Respond(uri, body);

    public void Record(string campaignType, DateOnly from, int days, decimal cost, decimal conversions, decimal value = 0)
    {
        var list = Days.TryGetValue(campaignType, out var existing) ? existing : Days[campaignType] = [];
        for (var offset = 0; offset < days; offset++) list.Add(new CareDay(from.AddDays(offset), cost / days, conversions / days, value / days));
    }

    public JsonObject Campaign(string campaignType) =>
        launch.Resources.First(pair => pair.Key.Contains("/campaigns/", StringComparison.Ordinal) && pair.Value["advertisingChannelType"]?.GetValue<string>() == campaignType.ToUpperInvariant()).Value;

    public JsonObject Budget(string campaignType) => launch.Resources[Campaign(campaignType)["campaignBudget"]!.GetValue<string>()];

    public decimal TotalBudget(string campaignType) => decimal.Parse(Budget(campaignType)["totalAmountMicros"]!.ToString(), CultureInfo.InvariantCulture) / 1_000_000m;

    public void BidToTarget(string campaignType, string strategy, decimal target)
    {
        var campaign = Campaign(campaignType);
        campaign.Remove("targetSpend");
        campaign.Remove("targetCpa");
        campaign.Remove("targetRoas");
        campaign[strategy == "target_cpa" ? "targetCpa" : "targetRoas"] = strategy == "target_cpa"
            ? new JsonObject { ["targetCpaMicros"] = ((long)(target * 1_000_000m)).ToString(CultureInfo.InvariantCulture) }
            : new JsonObject { ["targetRoas"] = target };
    }

    public JsonElement[] Writes() => [.. Mutations.Where(call => !call.GetProperty("validateOnly").GetBoolean())];

    private HttpResponseMessage? Handle(Uri uri, string body)
    {
        if (uri.Host != "googleads.googleapis.com") return null;
        if (uri.AbsolutePath.EndsWith("/recommendations:apply", StringComparison.Ordinal))
        {
            if (FailWrites) return Rejected();
            var name = JsonDocument.Parse(body).RootElement.GetProperty("operations")[0].GetProperty("resourceName").GetString()!;
            Applied.Add(name);
            Suggestions.RemoveAll(suggestion => Name(suggestion) == name);
            return Json(new { results = new[] { new { resourceName = name } } });
        }

        if (uri.AbsolutePath.EndsWith("/googleAds:mutate", StringComparison.Ordinal)) return Mutate(body);
        if (!uri.AbsolutePath.EndsWith("/googleAds:search", StringComparison.Ordinal)) return null;
        var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;
        var mine = query.Contains("FROM recommendation", StringComparison.Ordinal) || query.Contains("campaign_budget.total_amount_micros", StringComparison.Ordinal)
            || query.Contains("metrics.conversions_value", StringComparison.Ordinal) && query.Contains("segments.date,", StringComparison.Ordinal);
        if (!mine) return null;
        if (TransientReads > 0) { TransientReads--; return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }
        if (query.Contains("FROM recommendation", StringComparison.Ordinal) && ++SuggestionReads > 0)
            return Json(new
            {
                results = Suggestions.Select(suggestion => new
                {
                    recommendation = new
                    {
                        resourceName = Name(suggestion), type = suggestion.Type, campaign = Resource(suggestion.CampaignType),
                        impact = new
                        {
                            baseMetrics = new { costMicros = "10000000000", conversions = 20.0, conversionsValue = 0.0, clicks = "400" },
                            potentialMetrics = new
                            {
                                costMicros = ((long)((10000 + suggestion.ExtraCost) * 1_000_000m)).ToString(CultureInfo.InvariantCulture),
                                conversions = 20 + (double)suggestion.ExtraConversions, conversionsValue = 0.0, clicks = (400 + suggestion.ExtraClicks).ToString(CultureInfo.InvariantCulture),
                            },
                        },
                    },
                }),
            });
        var campaigns = launch.Resources.Where(pair => pair.Key.Contains("/campaigns/", StringComparison.Ordinal) && query.Contains("'" + pair.Key + "'", StringComparison.Ordinal)).ToList();
        if (query.Contains("campaign_budget.total_amount_micros", StringComparison.Ordinal))
            return Json(new
            {
                results = campaigns.Select(pair => new Dictionary<string, object>
                {
                    ["campaign"] = Settings(pair.Key, pair.Value),
                    ["campaignBudget"] = new { resourceName = pair.Value["campaignBudget"]!.GetValue<string>(), totalAmountMicros = launch.Resources[pair.Value["campaignBudget"]!.GetValue<string>()]["totalAmountMicros"]!.ToString() },
                }),
            });
        var range = Between().Match(query);
        var (from, to) = (DateOnly.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture), DateOnly.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture));
        return Json(new
        {
            results = campaigns.SelectMany(pair => Days.GetValueOrDefault(pair.Value["advertisingChannelType"]!.GetValue<string>().ToLowerInvariant(), [])
                .Where(day => day.Date >= from && day.Date <= to)
                .Select(day => new
                {
                    campaign = new { resourceName = pair.Key },
                    segments = new { date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                    metrics = new { costMicros = ((long)(day.Cost * 1_000_000m)).ToString(CultureInfo.InvariantCulture), conversions = (double)day.Conversions, conversionsValue = (double)day.Value },
                })),
        });
    }

    private HttpResponseMessage? Mutate(string body)
    {
        var json = JsonDocument.Parse(body).RootElement.Clone();
        var operations = json.GetProperty("mutateOperations").EnumerateArray().ToArray();
        var mine = operations.All(operation => operation.TryGetProperty("campaignBudgetOperation", out _)
            || operation.TryGetProperty("campaignOperation", out var campaign) && campaign.TryGetProperty("update", out var update) && !update.TryGetProperty("status", out _));
        if (!mine) return null;
        Mutations.Add(json);
        if (FailWrites) return Rejected();
        if (json.GetProperty("validateOnly").GetBoolean()) return Json(new { });
        var results = new List<object>();
        foreach (var operation in operations)
        {
            var kind = operation.EnumerateObject().Single();
            var update = JsonNode.Parse(kind.Value.GetProperty("update").GetRawText())!.AsObject();
            var name = update["resourceName"]!.GetValue<string>();
            var target = launch.Resources[name];
            foreach (var property in update.Where(property => property.Key != "resourceName").ToList())
            {
                if (property.Key is "targetCpa" or "targetRoas") { target.Remove("targetSpend"); target.Remove("targetCpa"); target.Remove("targetRoas"); }
                target[property.Key] = property.Value!.DeepClone();
            }

            results.Add(new Dictionary<string, object> { [kind.Name[..^"Operation".Length] + "Result"] = new { resourceName = name } });
        }

        return Json(new { mutateOperationResponses = results });
    }

    private static object Settings(string name, JsonObject campaign)
    {
        var settings = new JsonObject
        {
            ["resourceName"] = name,
            ["status"] = campaign["status"]?.GetValue<string>() ?? "PAUSED",
            ["biddingStrategyType"] = campaign.ContainsKey("targetCpa") ? "TARGET_CPA" : campaign.ContainsKey("targetRoas") ? "TARGET_ROAS" : "TARGET_SPEND",
        };
        if (campaign["targetCpa"] is { } cpa) settings["targetCpa"] = cpa.DeepClone();
        if (campaign["targetRoas"] is { } roas) settings["targetRoas"] = roas.DeepClone();
        return settings;
    }

    private string Resource(string campaignType) => launch.Resources.First(pair => pair.Value == Campaign(campaignType)).Key;

    private static string Name(CareSuggestion suggestion) => $"customers/1111111111/recommendations/{suggestion.Id}";

    private static HttpResponseMessage Rejected() => new(HttpStatusCode.BadRequest)
    {
        Content = JsonContent.Create(new { error = new { details = new[] { new { errors = new[] { new { errorCode = new { campaignBudgetError = "BUDGET_TOO_LOW" } } } } } } }),
    };

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    [GeneratedRegex(@"BETWEEN '(\d{4}-\d{2}-\d{2})' AND '(\d{4}-\d{2}-\d{2})'")]
    private static partial Regex Between();
}
