using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.Triggers;

public sealed class FakePlan
{
    public bool Fails { get; set; }

    public int Drafts { get; private set; }

    public List<JsonElement> Requests { get; } = [];

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) =>
        {
            if (path != "/plan/trigger-brief")
            {
                return previous(path, body);
            }

            lock (Requests)
            {
                Requests.Add(body);
                if (Fails)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                }

                Drafts++;
            }

            var regions = body.GetProperty("trigger").GetProperty("regions").EnumerateArray().Select(region => region.GetString()).ToArray();
            return FakeAgents.Json(new
            {
                brief = new
                {
                    title = "Monsoon runners",
                    goal = "Sell waterproof shoes while the rain lasts.",
                    audience = "Commuters caught in the rain.",
                    keyMessage = "Stay dry on the way to work.",
                    offer = (string?)null,
                    regions,
                    productIds = new[] { "p1", "p9" },
                    channels = new[] { "search", "shopping" },
                    timing = "Run while the rain lasts.",
                },
                model = "gemini-test",
                promptVersion = "plan-trigger-brief/1",
            });
        };
    }
}

internal static class Triggered
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 14, 4, 30, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Base(Guid brandId) => $"/api/brands/{brandId}/triggers";

    public static object RainRule(string[] regions, double threshold = 50, int withinDays = 3, int cooldownHours = 72, string category = Radar.RainCategory, string name = "Monsoon rain") => new
    {
        name,
        source = "weather",
        condition = new { kind = "heavy_rain", threshold, withinDays },
        regions,
        category,
        cooldownHours,
    };

    public static object StockRule(long minUnits, string category = Radar.RainCategory, string[]? regions = null) => new
    {
        name = "Clear rain stock",
        source = "stock",
        condition = new { minUnits },
        regions = regions ?? [],
        category,
    };

    public static object FestivalRule(Guid eventId, int daysBefore = 7, string[]? regions = null, string category = Radar.RainCategory) => new
    {
        name = "Festival push",
        source = "festival",
        condition = new { eventId, daysBefore },
        regions = regions ?? [],
        category,
    };

    public static async Task<JsonElement> CreateRuleAsync(HttpClient admin, Guid brandId, object rule)
    {
        using var response = await admin.PostAsJsonAsync($"{Base(brandId)}/rules", rule, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("rule");
    }

    public static async Task<Guid> AddFestivalAsync(HttpClient admin, Guid brandId, string name, DateOnly startsOn, DateOnly? endsOn = null, string[]? regions = null)
    {
        using var response = await admin.PostAsJsonAsync($"{Base(brandId)}/festivals", new { name, startsOn, endsOn, regions = regions ?? [] }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> RuleAsync(HttpClient browser, Guid brandId, Guid ruleId) =>
        await browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/rules/{ruleId}", Token);

    public static async Task<JsonElement> CheckAsync(KairoWorkerFactory worker, Guid? brandId = null, bool force = false)
    {
        using var response = await worker.CheckTriggersAsync(brandId, force);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static JsonElement Result(JsonElement run, Guid brandId) =>
        run.GetProperty("results").EnumerateArray().Single(result => result.GetProperty("brandId").GetGuid() == brandId);

    public static Task<JsonElement[]> TriggeredCardsAsync(HttpClient browser, Guid brandId) =>
        Radar.CardsAsync(browser, brandId, "?triggered=true");
}
