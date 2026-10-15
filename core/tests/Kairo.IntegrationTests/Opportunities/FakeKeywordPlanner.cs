using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Opportunities;

public sealed record PlannerCall(string Method, string Customer, JsonElement Body);

public sealed class FakeKeywordPlanner
{
    public const string India = "2356";
    public const string Maharashtra = "21342";
    public const string Kerala = "21333";
    public const string Karnataka = "21331";

    private static readonly string[] MonthNames =
    [
        "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE",
        "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER",
    ];

    public ConcurrentDictionary<string, long[]> Volumes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, (long Low, long High)> Bids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, double> RegionLastMonth { get; } = new() { [Kerala] = 3.0 };

    public ConcurrentQueue<PlannerCall> Calls { get; } = new();

    public string? AccessError { get; set; }

    public bool Fails { get; set; }

    public HashSet<string> FailingCustomers { get; } = [];

    public HashSet<string> FailingRegions { get; } = [];

    public (string Id, string Name)[] States { get; set; } =
    [
        (Maharashtra, "Maharashtra"),
        (Kerala, "Kerala"),
        (Karnataka, "Karnataka"),
    ];

    public static long[] Rising(long from, long to, int months = 24) =>
        Enumerable.Range(0, months).Select(i => i < months - 1 ? from : to).ToArray();

    public static long[] Flat(long searches, int months = 24) => Enumerable.Repeat(searches, months).ToArray();

    public IReadOnlyList<string> SeedsSent() =>
        Calls.Where(call => call.Method == "ideas")
            .SelectMany(call => call.Body.GetProperty("keywordSeed").GetProperty("keywords").EnumerateArray().Select(seed => seed.GetString()!))
            .ToList();

    public HttpResponseMessage? Handle(string path, string body)
    {
        var method = path.EndsWith(":generateKeywordIdeas", StringComparison.Ordinal) ? "ideas"
            : path.EndsWith(":generateKeywordHistoricalMetrics", StringComparison.Ordinal) ? "history"
            : path.EndsWith("/googleAds:search", StringComparison.Ordinal) && body.Contains("FROM geo_target_constant", StringComparison.Ordinal) ? "geo"
            : null;
        if (method is null)
        {
            return null;
        }

        var customer = path.Split('/')[3].Split(':')[0];
        using var document = JsonDocument.Parse(body);
        Calls.Enqueue(new PlannerCall(method, customer, document.RootElement.Clone()));
        if (AccessError is { } error)
        {
            return Json(HttpStatusCode.Forbidden, new
            {
                error = new
                {
                    code = 403,
                    message = "The caller does not have permission",
                    status = "PERMISSION_DENIED",
                    details = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["@type"] = "type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
                            ["errors"] = new[] { new { errorCode = new { authorizationError = error }, message = "No access." } },
                        },
                    },
                },
            });
        }

        if (Fails || FailingCustomers.Contains(customer)
            || (method == "history" && FailingRegions.Contains(document.RootElement.GetProperty("geoTargetConstants")[0].GetString()!.Split('/')[1])))
        {
            return Json(HttpStatusCode.InternalServerError, new { error = new { code = 500, message = "Internal error encountered.", status = "INTERNAL" } });
        }

        return method switch
        {
            "geo" => Json(HttpStatusCode.OK, new
            {
                results = new[] { new { geoTargetConstant = new { id = India, name = "India", targetType = "Country" } } }
                    .Concat(States.Select(state => new { geoTargetConstant = new { id = state.Id, name = state.Name, targetType = "State" } }))
                    .ToArray(),
            }),
            "ideas" => Ideas(document.RootElement),
            _ => History(document.RootElement),
        };
    }

    private HttpResponseMessage Ideas(JsonElement request)
    {
        var end = EndOf(request);
        return Json(HttpStatusCode.OK, new
        {
            results = Volumes.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new
            {
                text = entry.Key,
                keywordIdeaMetrics = IdeaMetrics(entry.Key, entry.Value, end),
            }).ToArray(),
        });
    }

    private Dictionary<string, object> IdeaMetrics(string keyword, long[] volumes, DateOnly end)
    {
        var metrics = new Dictionary<string, object>
        {
            ["avgMonthlySearches"] = volumes.Last().ToString(),
            ["monthlySearchVolumes"] = Months(volumes, end, 1),
        };
        if (Bids.TryGetValue(keyword, out var bids))
        {
            metrics["lowTopOfPageBidMicros"] = bids.Low.ToString();
            metrics["highTopOfPageBidMicros"] = bids.High.ToString();
        }

        return metrics;
    }

    private HttpResponseMessage History(JsonElement request)
    {
        var end = EndOf(request);
        var region = request.GetProperty("geoTargetConstants")[0].GetString()!.Split('/')[1];
        var lastMonth = RegionLastMonth.GetValueOrDefault(region, 1.0);
        var wanted = request.GetProperty("keywords").EnumerateArray().Select(keyword => keyword.GetString()!).ToList();
        return Json(HttpStatusCode.OK, new
        {
            results = wanted.Where(Volumes.ContainsKey).Select(keyword => new
            {
                text = keyword,
                keywordMetrics = new { monthlySearchVolumes = Months(Volumes[keyword].Select(v => v / 10).ToArray(), end, lastMonth) },
            }).ToArray(),
        });
    }

    private static object[] Months(long[] volumes, DateOnly end, double lastMonth) =>
        volumes.Select((searches, index) =>
        {
            var month = end.AddMonths(index - volumes.Length + 1);
            var value = index == volumes.Length - 1 ? (long)(searches * lastMonth) : searches;
            return (object)new { month = MonthNames[month.Month - 1], year = month.Year.ToString(), monthlySearches = value.ToString() };
        }).ToArray();

    private static DateOnly EndOf(JsonElement request)
    {
        var end = request.GetProperty("historicalMetricsOptions").GetProperty("yearMonthRange").GetProperty("end");
        return new DateOnly(end.GetProperty("year").GetInt32(), Array.IndexOf(MonthNames, end.GetProperty("month").GetString()) + 1, 1);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };
}
