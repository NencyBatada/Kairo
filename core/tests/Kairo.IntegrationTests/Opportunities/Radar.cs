using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;

namespace Kairo.IntegrationTests.Opportunities;

public sealed record RadarBrand(Member Admin, Member Marketer, Guid BrandId, string Handle);

public sealed class FakeSense
{
    public List<object> Topics { get; } =
    [
        new { name = "Monsoon footwear", keywords = new[] { "rain boots", "waterproof shoes" }, categories = new[] { "Shoes > Rain" }, fit = 0.9, fitReason = "Waterproof runners are in stock." },
    ];

    public Func<string, object[]> Reasons { get; set; } = topic =>
    [
        new { text = $"The monsoon arrived early, lifting searches for {topic}.", sources = new[] { new { title = "Weather news", url = "https://weather.example/monsoon" } } },
    ];

    public bool ExplainFails { get; set; }

    public bool MatchFails { get; set; }

    public void Attach(FakeAgents agents) => agents.Respond = (path, body) => path switch
    {
        "/sense/match" when MatchFails => new HttpResponseMessage(HttpStatusCode.BadGateway),
        "/sense/match" => FakeAgents.Json(new { topics = Topics, model = "gemini-test", promptVersion = "sense-match/1" }),
        "/sense/explain" when ExplainFails => new HttpResponseMessage(HttpStatusCode.BadGateway),
        "/sense/explain" => FakeAgents.Json(new
        {
            topics = body.GetProperty("topics").EnumerateArray()
                .Select(topic => new { id = topic.GetProperty("id").GetString(), reasons = Reasons(topic.GetProperty("name").GetString()!) })
                .ToArray(),
            model = "gemini-test",
            promptVersion = "sense-explain/1",
        }),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };
}

internal static class Radar
{
    public const string RainCategory = "Shoes > Rain";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Base(Guid brandId) => $"/api/brands/{brandId}/opportunities";

    public static string Catalogue(string availability = "in_stock") => Catalogues.Csv(
        Catalogues.Row("RAIN-1", availability, "Monsoon Waterproof Runner", category: RainCategory),
        Catalogues.Row("RAIN-2", availability, "Rain Boots Classic", category: RainCategory),
        Catalogues.Row("SCHOOL-1", "in_stock", "School Shoes Black", category: "Shoes > School"));

    public static void PlanDemand(FakeKeywordPlanner planner)
    {
        planner.Volumes["rain boots"] = FakeKeywordPlanner.Rising(1000, 3000);
        planner.Volumes["waterproof shoes"] = FakeKeywordPlanner.Rising(2000, 4000);
        planner.Volumes["school shoes"] = FakeKeywordPlanner.Flat(5000);
    }

    public static async Task<RadarBrand> BrandAsync(KairoApiFactory api, bool connectAds = true, bool catalogue = true, string account = "1111111111")
    {
        var admin = await Workspaces.SignInAsync(api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        if (connectAds)
        {
            await Connections.CompleteAsync(api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));
            (await admin.Browser.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = account }, Token))
                .EnsureSuccessStatusCode();
        }

        if (catalogue)
        {
            await Catalogues.ImportAsync(admin.Browser, brandId, Catalogue());
        }

        var marketer = await Workspaces.JoinAsync(api, admin.Browser, brandId, "marketer");
        return new RadarBrand(admin, marketer, brandId, handle);
    }

    public static async Task<JsonElement> RefreshAsync(KairoWorkerFactory worker, Guid? brandId = null)
    {
        worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await worker.RefreshOpportunitiesAsync(brandId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static JsonElement Result(JsonElement run, Guid brandId) =>
        run.GetProperty("results").EnumerateArray().Single(result => result.GetProperty("brandId").GetGuid() == brandId);

    public static string Outcome(JsonElement run, Guid brandId) => Result(run, brandId).GetProperty("outcome").GetString()!;

    public static async Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        await browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}{query}", Token);

    public static async Task<JsonElement[]> CardsAsync(HttpClient browser, Guid brandId, string query = "") =>
        (await ListAsync(browser, brandId, query)).GetProperty("items").EnumerateArray().ToArray();

    public static async Task<JsonElement> OnlyCardAsync(HttpClient browser, Guid brandId, string query = "") =>
        Assert.Single(await CardsAsync(browser, brandId, query));

    public static async Task<JsonElement> DetailAsync(HttpClient browser, Guid brandId, Guid opportunityId) =>
        await browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{opportunityId}", Token);

    public static Task<HttpResponseMessage> DecideAsync(HttpClient browser, Guid brandId, Guid opportunityId, string decision, object? body = null) =>
        browser.PostAsJsonAsync($"{Base(brandId)}/{opportunityId}/{decision}", body ?? new { }, Token);

    public static async Task<HttpResponseMessage> AddTopicAsync(HttpClient browser, Guid brandId, string phrase, string kind) =>
        await browser.PostAsJsonAsync($"{Base(brandId)}/topics", new { phrase, kind }, Token);
}
