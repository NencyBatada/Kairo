using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Opportunities;
using Kairo.IntegrationTests.Triggers;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CampaignWorld : IAsyncDisposable
{
    private static readonly (string Id, string Name, string State)[] Cities =
    [
        ("1007785", "Mumbai", "Maharashtra"),
        ("1007788", "Pune", "Maharashtra"),
        ("1007790", "Nagpur", "Maharashtra"),
        ("1007791", "Nashik", "Maharashtra"),
        ("1007809", "Chennai", "Tamil Nadu"),
    ];

    private CampaignWorld(string database, KairoApiFactory api, KairoWorkerFactory worker, RadarBrand brand, Member approver, FakeBriefPlan plan, FakeProof proof, FakeCreate create)
    {
        Proof = proof;
        Create = create;
        Database = database;
        Api = api;
        Worker = worker;
        Brand = brand;
        Approver = approver;
        Plan = plan;
    }

    public string Database { get; }

    public KairoApiFactory Api { get; }

    public KairoWorkerFactory Worker { get; }

    public RadarBrand Brand { get; }

    public Member Approver { get; }

    public FakeBriefPlan Plan { get; }

    public FakeProof Proof { get; }

    public FakeCreate Create { get; }

    public Guid BrandId => Brand.BrandId;

    public HttpClient Marketer => Brand.Marketer.Browser;

    public HttpClient Admin => Brand.Admin.Browser;

    public static async Task<CampaignWorld> CreateAsync(SqlServer sqlServer, Action<FakeSense, FakeKeywordPlanner>? demand = null)
    {
        var database = await IsolatedDatabase.CreateAsync(sqlServer);
        var api = new KairoApiFactory(database);
        var worker = new KairoWorkerFactory(database, api.GoogleApis, api.Clock.GetUtcNow());
        var sense = new FakeSense();
        sense.Attach(worker.Agents);
        var plan = new FakeBriefPlan();
        plan.Attach(api.Agents);
        plan.Attach(worker.Agents);
        var proof = new FakeProof();
        proof.Attach(api.Agents);
        proof.Attach(worker.Agents);
        var create = new FakeCreate();
        create.Attach(api.Agents);
        create.Attach(worker.Agents);
        Radar.PlanDemand(api.GoogleApis.Keywords);
        api.GoogleApis.Keywords.Bids["rain boots"] = (10_000_000, 30_000_000);
        api.GoogleApis.Keywords.Bids["waterproof shoes"] = (14_000_000, 42_000_000);
        demand?.Invoke(sense, api.GoogleApis.Keywords);
        var brand = await Radar.BrandAsync(api);
        var approver = await Workspaces.JoinAsync(api, brand.Admin.Browser, brand.BrandId, "approver");
        await Radar.RefreshAsync(worker);
        var world = new CampaignWorld(database, api, worker, brand, approver, plan, proof, create);
        await world.AssignApproversAsync(approver.UserId, approver.UserId, approver.UserId);
        return world;
    }

    public async Task<Guid> OpportunityAsync() =>
        (await Radar.OnlyCardAsync(Marketer, BrandId)).GetProperty("id").GetGuid();

    public async Task<Guid> AcceptAsync(Guid? opportunityId = null)
    {
        var id = opportunityId ?? await OpportunityAsync();
        using var response = await Radar.DecideAsync(Marketer, BrandId, id, "accept");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token)).GetProperty("roomId").GetGuid();
    }

    public async Task<Guid> OpenWithBriefAsync()
    {
        var roomId = await AcceptAsync();
        Assert.Equal(1, await RunStagesAsync());
        Assert.Equal(1, await RunStagesAsync());
        return roomId;
    }

    public async Task<int> RunStagesAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StageDispatcher>().RunDueAsync(Campaigns.Token);
    }

    public async Task<CampaignAdvance> SignalAsync(Guid roomId, string signal)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(BrandId, Campaigns.Token);
        return await scope.ServiceProvider.GetRequiredService<ICampaignLifecycle>()
            .AdvanceAsync(roomId, signal, AuditActor.Agent("Check agent", "check"), Campaigns.Token);
    }

    public async Task AssignApproversAsync(Guid brief, Guid assets, Guid launch)
    {
        using var response = await Admin.PutAsJsonAsync($"/api/brands/{BrandId}/approval-settings", new
        {
            briefApprovers = new[] { brief },
            assetApprovers = new[] { assets },
            launchApprovers = new[] { launch },
            budgetOwners = new[] { brief },
            budgetMoveApprovers = new[] { brief },
            targetChangeApprovers = new[] { brief },
            googleSuggestionApprovers = new[] { brief },
            budgetLimit = 200000m,
        }, Campaigns.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task<JsonElement> ApproveBriefAsync(Guid roomId)
    {
        var detail = await Campaigns.DetailAsync(Marketer, BrandId, roomId);
        using var requested = await Campaigns.PostAsync(Marketer, BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token);
        using var approved = await Campaigns.PostAsync(Approver.Browser, BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending) });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return await approved.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token);
    }

    public async Task<JsonElement> LaunchAsync(Guid roomId)
    {
        await ApproveBriefAsync(roomId);
        Assert.Equal(CampaignAdvance.Moved, await SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var review = await Campaigns.DetailAsync(Approver.Browser, BrandId, roomId);
        using var assets = await Campaigns.PostAsync(Approver.Browser, BrandId, roomId, "approve", new { gate = "assets", version = Campaigns.Version(review) });
        Assert.Equal(HttpStatusCode.OK, assets.StatusCode);
        var ready = await assets.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token);
        using var requested = await Campaigns.PostAsync(Marketer, BrandId, roomId, "approval-requests", new { gate = "launch", version = Campaigns.Version(ready) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var waiting = await requested.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token);
        using var launched = await Campaigns.PostAsync(Approver.Browser, BrandId, roomId, "approve", new { gate = "launch", version = Campaigns.Version(waiting) });
        Assert.Equal(HttpStatusCode.OK, launched.StatusCode);
        return await launched.Content.ReadFromJsonAsync<JsonElement>(Campaigns.Token);
    }

    public void LoadCityHistory()
    {
        var days = Enumerable.Range(1, 120).Select(offset => Today().AddDays(-offset)).ToList();
        Api.GoogleApis.BigQuery.Selects["ads_city_daily"] =
        [
            .. days.SelectMany(day => Cities.Select(city => new[] { Campaigns.Iso(day), city.Id, city.Name, city.State, "4", "INR" })),
        ];
        Api.GoogleApis.BigQuery.Selects["sales_daily"] = [.. days.Select(day => new[] { Campaigns.Iso(day), "Mumbai", "10", "25000" })];
        foreach (var city in Cities)
        {
            Api.GoogleApis.Keywords.RegionLastMonth[city.Id] = 1.0;
        }
    }

    public DateOnly Today() =>
        DateOnly.FromDateTime(Api.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5)).DateTime);

    public async ValueTask DisposeAsync()
    {
        await Worker.DisposeAsync();
        await Api.DisposeAsync();
    }
}

internal static class Campaigns
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Base(Guid brandId) => $"/api/brands/{brandId}/campaigns";

    public static Task<JsonElement> DetailAsync(HttpClient browser, Guid brandId, Guid roomId) =>
        browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{roomId}", Token);

    public static async Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        await browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}{query}", Token);

    public static async Task<JsonElement[]> CardsAsync(HttpClient browser, Guid brandId, string query = "") =>
        [.. (await ListAsync(browser, brandId, query)).GetProperty("campaigns").EnumerateArray()];

    public static Task<HttpResponseMessage> PostAsync(HttpClient browser, Guid brandId, Guid roomId, string action, object body) =>
        browser.PostAsJsonAsync($"{Base(brandId)}/{roomId}/{action}", body, Token);

    public static async Task<JsonElement> ActivityAsync(HttpClient browser, Guid brandId, Guid roomId, string query = "") =>
        await browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{roomId}/activity{query}", Token);

    public static int Version(JsonElement detail) => detail.GetProperty("version").GetInt32();

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static Task<JsonElement> PlanAsync(HttpClient browser, Guid brandId, Guid roomId) =>
        browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{roomId}/proof-plan", Token);

    public static Task<JsonElement> BriefAsync(HttpClient browser, Guid brandId, Guid roomId) =>
        browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{roomId}/brief", Token);

    public static Task<HttpResponseMessage> EditBriefAsync(HttpClient browser, Guid brandId, Guid roomId, object body) =>
        browser.PatchAsJsonAsync($"{Base(brandId)}/{roomId}/brief", body, Token);

    public static Task<JsonElement> AssetsAsync(HttpClient browser, Guid brandId, Guid roomId) =>
        browser.GetFromJsonAsync<JsonElement>($"{Base(brandId)}/{roomId}/assets", Token);

    public static Task<HttpResponseMessage> EditAssetAsync(HttpClient browser, Guid brandId, Guid roomId, Guid assetId, object body) =>
        browser.PatchAsJsonAsync($"{Base(brandId)}/{roomId}/assets/{assetId}", body, Token);

    public static Task<HttpResponseMessage> AssetActionAsync(HttpClient browser, Guid brandId, Guid roomId, Guid assetId, string action, object body) =>
        browser.PostAsJsonAsync($"{Base(brandId)}/{roomId}/assets/{assetId}/{action}", body, Token);

    public static JsonElement[] AllAssets(JsonElement set) =>
        [.. set.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("assets").EnumerateArray())];

    public static JsonElement Asset(JsonElement set, string kind) =>
        AllAssets(set).Single(asset => asset.GetProperty("kind").GetString() == kind);

    public static string Stage(JsonElement detail) => detail.GetProperty("stage").GetString()!;

    public static string Status(JsonElement detail) => detail.GetProperty("status").GetString()!;

    public static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();
}
