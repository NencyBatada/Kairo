using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Launch;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.IntegrationTests.Opportunities;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Launch;

public sealed class StockPauseTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string Search = "customers/1111111111/campaigns/1";
    private const string DemandGen = "customers/1111111111/campaigns/2";
    private const string Shopping = "customers/1111111111/campaigns/3";
    private const string RunnerAd = "customers/1111111111/adGroupAds/10~1";
    private const string PairAd = "customers/1111111111/adGroupAds/10~2";
    private const string SchoolAd = "customers/1111111111/adGroupAds/10~3";
    private const string PlainAd = "customers/1111111111/adGroupAds/10~4";
    private const string BootAd = "customers/1111111111/adGroupAds/20~1";
    private const string ShoppingAd = "customers/1111111111/adGroupAds/30~1";
    private readonly FakeLaunchGoogle _google = new();
    private string _database = null!;
    private KairoApiFactory _api = null!;
    private RadarBrand _brand = null!;
    private Guid _room;
    private Dictionary<string, Guid> _products = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _brand = await Radar.BrandAsync(_api);
        _api.GoogleApis.LaunchResponse = _google.Respond;
        _products = await ProductsAsync();
        _room = await SeedLaunchAsync(_api.Clock.GetUtcNow());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_sold_out_product_pauses_its_search_ad_and_restock_resumes_it_without_touching_shopping()
    {
        await StockAsync(rain1: "out_of_stock");
        Assert.Equal(1, await RunAsync());

        Assert.Equal("PAUSED", Status(RunnerAd));
        Assert.Null(Status(PairAd));
        Assert.Null(Status(ShoppingAd));
        var write = Assert.Single(Writes());
        Assert.Equal([RunnerAd], Updated(write));
        var pause = Assert.Single((await LogAsync()).GetProperty("pauses").EnumerateArray());
        Assert.Equal(("paused", "search", true), (pause.GetProperty("state").GetString(), pause.GetProperty("campaignType").GetString(), pause.GetProperty("canUndo").GetBoolean()));
        Assert.Equal([_products["RAIN-1"].ToString()], pause.GetProperty("productIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Contains(await NotificationRows.HeldAsync(_database, _brand.BrandId), row => row.Kind == "stock.pause_changed" && row.UserId == _brand.Marketer.UserId);
        Assert.Contains("launch.ad_paused_stock", await AuditAsync());

        Assert.Equal(0, await RunAsync());
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        Assert.Single(Writes());

        await StockAsync();
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        Assert.Equal("ENABLED", Status(RunnerAd));
        Assert.Equal("resumed", (await LogAsync()).GetProperty("pauses")[0].GetProperty("state").GetString());
        Assert.Contains("launch.ad_resumed_stock", await AuditAsync());
        Assert.Equal(2, Writes().Length);
    }

    [Fact]
    public async Task A_failed_google_change_records_nothing_and_is_retried_on_the_next_check()
    {
        await StockAsync(rain1: "out_of_stock");
        _google.FailAdStatus = true;
        Assert.Equal(1, await RunAsync());
        Assert.Null(Status(RunnerAd));
        Assert.Empty((await LogAsync()).GetProperty("pauses").EnumerateArray());

        _google.FailAdStatus = false;
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        Assert.Equal("PAUSED", Status(RunnerAd));
        var pause = (await LogAsync()).GetProperty("pauses")[0];

        _google.FailAdStatus = true;
        using var undo = await _brand.Marketer.Browser.PostAsJsonAsync($"{Base}/{pause.GetProperty("id").GetGuid()}/undo", new { version = pause.GetProperty("version").GetInt32() }, Token);
        Assert.Equal(HttpStatusCode.BadGateway, undo.StatusCode);
        Assert.Equal("paused", (await LogAsync()).GetProperty("pauses")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_worker_job_checks_stock_for_every_due_workspace()
    {
        await StockAsync(rain1: "out_of_stock");
        await using var worker = new KairoWorkerFactory(_database, _api.GoogleApis, _api.Clock.GetUtcNow());
        using var client = worker.CreateClient();

        using var response = await client.PostAsync("/jobs/launches/stock", null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("processed").GetInt32());
        Assert.Equal("PAUSED", Status(RunnerAd));
    }

    [Fact]
    public async Task Pausing_more_than_thirty_percent_of_a_campaign_is_held_until_an_admin_confirms_or_dismisses()
    {
        OperationsMail.Enable(_api.Services);
        await StockAsync(rain1: "out_of_stock", rain2: "out_of_stock");
        Assert.Equal(1, await RunAsync());

        Assert.Empty(Writes());
        var pauses = (await LogAsync()).GetProperty("pauses").EnumerateArray().ToArray();
        Assert.Equal(3, pauses.Length);
        Assert.All(pauses, pause => Assert.Equal("held", pause.GetProperty("state").GetString()));
        var searchBatch = pauses.First(pause => pause.GetProperty("campaignType").GetString() == "search").GetProperty("batchId").GetGuid();
        var demandBatch = pauses.Single(pause => pause.GetProperty("campaignType").GetString() == "demand_gen").GetProperty("batchId").GetGuid();
        Assert.NotEqual(searchBatch, demandBatch);
        Assert.Equal(2, (await NotificationRows.EmailsAsync(_database, _brand.BrandId, "Operations")).Count);
        var held = (await NotificationRows.NotificationsAsync(_database, _brand.BrandId)).Where(row => row.Kind == "mass_pause.held").ToArray();
        Assert.Contains(held, row => row.UserId == _brand.Admin.UserId && row.Body.StartsWith("2 of 4 ads", StringComparison.Ordinal));
        Assert.Contains(held, row => row.UserId == _brand.Marketer.UserId);

        using (var forbidden = await _brand.Marketer.Browser.PostAsync($"{Base}/batches/{searchBatch}/confirm", null, Token))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var confirmed = await _brand.Admin.Browser.PostAsync($"{Base}/batches/{searchBatch}/confirm", null, Token))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(("PAUSED", "PAUSED"), (Status(RunnerAd), Status(PairAd)));
        Assert.All((await NotificationRows.NotificationsAsync(_database, _brand.BrandId)).Where(row => row.Subject == StockPauseWorker.Subject(searchBatch)), row => Assert.NotNull(row.ResolvedAt));
        using (var again = await _brand.Admin.Browser.PostAsync($"{Base}/batches/{searchBatch}/confirm", null, Token))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        using (var dismissed = await _brand.Admin.Browser.PostAsync($"{Base}/batches/{demandBatch}/dismiss", null, Token))
            Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        Assert.Null(Status(BootAd));
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        Assert.Null(Status(BootAd));
        var states = (await LogAsync()).GetProperty("pauses").EnumerateArray().Select(pause => pause.GetProperty("state").GetString()).Order().ToArray();
        Assert.Equal(["dismissed", "paused", "paused"], states);
        Assert.Contains("launch.mass_pause_confirmed", await AuditAsync());
        Assert.Contains("launch.mass_pause_dismissed", await AuditAsync());
    }

    [Fact]
    public async Task A_held_pause_is_cancelled_when_stock_returns_before_anyone_decides()
    {
        await StockAsync(rain1: "out_of_stock", rain2: "out_of_stock");
        await RunAsync();
        await StockAsync();
        _api.Clock.Advance(StockPauser.Interval);
        await RunAsync();
        Assert.All((await LogAsync()).GetProperty("pauses").EnumerateArray(), pause => Assert.Equal("cancelled", pause.GetProperty("state").GetString()));
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task A_stale_feed_stops_pauses_and_resumes_and_alerts_admins_once()
    {
        OperationsMail.Enable(_api.Services);
        await StockAsync(rain1: "out_of_stock");
        _api.Clock.Advance(StockPauseWorker.StaleAfter + TimeSpan.FromMinutes(1));
        Assert.Equal(1, await RunAsync());
        Assert.Empty(Writes());
        Assert.True((await LogAsync()).GetProperty("feedStale").GetBoolean());
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        var alerts = (await NotificationRows.NotificationsAsync(_database, _brand.BrandId)).Where(row => row.Kind == "connection.problem" && row.Title == "Stock pauses are on hold").ToArray();
        Assert.Equal(_brand.Admin.UserId, Assert.Single(alerts).UserId);
        Assert.Single(await NotificationRows.EmailsAsync(_database, _brand.BrandId, "Operations"));

        await StockAsync(rain1: "out_of_stock");
        _api.Clock.Advance(StockPauser.Interval);
        Assert.Equal(1, await RunAsync());
        Assert.Equal("PAUSED", Status(RunnerAd));
        Assert.False((await LogAsync()).GetProperty("feedStale").GetBoolean());
    }

    [Fact]
    public async Task Undoing_a_pause_resumes_the_ad_and_keeps_it_running_until_the_product_sells_out_again()
    {
        await StockAsync(rain1: "out_of_stock");
        await RunAsync();
        var pause = (await LogAsync()).GetProperty("pauses")[0];
        var path = $"{Base}/{pause.GetProperty("id").GetGuid()}/undo";
        using (var stale = await _brand.Marketer.Browser.PostAsJsonAsync(path, new { version = 99 }, Token))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var undone = await _brand.Marketer.Browser.PostAsJsonAsync(path, new { version = pause.GetProperty("version").GetInt32() }, Token))
            Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal("ENABLED", Status(RunnerAd));
        var row = (await LogAsync()).GetProperty("pauses")[0];
        Assert.Equal(("undone", false), (row.GetProperty("state").GetString(), row.GetProperty("canUndo").GetBoolean()));
        Assert.Equal(_brand.Marketer.UserId, row.GetProperty("decidedBy").GetGuid());
        using (var again = await _brand.Marketer.Browser.PostAsJsonAsync(path, new { version = row.GetProperty("version").GetInt32() }, Token))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("launch.stock_pause_undone", await AuditAsync());

        _api.Clock.Advance(StockPauser.Interval);
        await RunAsync();
        Assert.Equal("ENABLED", Status(RunnerAd));

        _api.Clock.Advance(TimeSpan.FromMinutes(1));
        await StockAsync();
        _api.Clock.Advance(TimeSpan.FromMinutes(1));
        await StockAsync(rain1: "out_of_stock");
        _api.Clock.Advance(StockPauser.Interval);
        await RunAsync();
        Assert.Equal("PAUSED", Status(RunnerAd));
    }

    [Fact]
    public async Task Ended_campaigns_and_other_workspaces_are_left_alone()
    {
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
            var record = await db.Launches.SingleAsync(row => row.RoomId == _room, Token);
            var plan = JsonSerializer.Deserialize<LaunchPlan>(record.Plan, JsonSerializerOptions.Web)!;
            record.Plan = JsonSerializer.Serialize(plan with { End = plan.Start.AddDays(-1) }, JsonSerializerOptions.Web);
            await db.SaveChangesAsync(Token);
        }

        await StockAsync(rain1: "out_of_stock");
        await RunAsync();
        Assert.Empty(Writes());
        Assert.Empty((await LogAsync()).GetProperty("pauses").EnumerateArray());

        await using var other = _api.Services.CreateAsyncScope();
        await other.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await other.ServiceProvider.GetRequiredService<LaunchDbContext>().StockWatches.ToListAsync(Token));
    }

    [Fact]
    public async Task The_additive_migration_keeps_existing_launches_and_adds_the_stock_tables()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016114802_LaunchRetryHealth", Token);
        await migrator.MigrateAsync(cancellationToken: Token);
        Assert.Equal("live", (await db.Launches.AsNoTracking().SingleAsync(row => row.RoomId == _room, Token)).Status);
        await StockAsync(rain1: "out_of_stock");
        Assert.Equal(1, await RunAsync());
        Assert.Equal("PAUSED", Status(RunnerAd));
    }

    private string Base => $"/api/brands/{_brand.BrandId}/campaigns/{_room}/stock-pauses";

    private async Task<JsonElement> LogAsync() => await _brand.Marketer.Browser.GetFromJsonAsync<JsonElement>(Base, Token);

    private async Task StockAsync(string rain1 = "in_stock", string rain2 = "in_stock")
    {
        var import = await Catalogues.ImportAsync(_brand.Admin.Browser, _brand.BrandId, Catalogues.Csv(
            Catalogues.Row("RAIN-1", rain1, "Monsoon Waterproof Runner", category: Radar.RainCategory),
            Catalogues.Row("RAIN-2", rain2, "Rain Boots Classic", category: Radar.RainCategory),
            Catalogues.Row("SCHOOL-1", "in_stock", "School Shoes Black", category: "Shoes > School")));
        Assert.Contains(import.GetProperty("status").GetString(), new[] { "succeeded", "unchanged" });
    }

    private async Task<int> RunAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StockPauser>().RunDueAsync(Token);
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_brand.BrandId, Token);
        return scope;
    }

    private string? Status(string resource) => _google.Resources[resource]["status"]?.GetValue<string>();

    private JsonElement[] Writes() => _google.Mutations.Where(call => !call.GetProperty("validateOnly").GetBoolean()).ToArray();

    private static string[] Updated(JsonElement call) => [.. call.GetProperty("mutateOperations").EnumerateArray()
        .Select(operation => operation.GetProperty("adGroupAdOperation").GetProperty("update").GetProperty("resourceName").GetString()!)];

    private async Task<Dictionary<string, Guid>> ProductsAsync()
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; SELECT OfferId, Id FROM catalogue.Products WHERE BrandId = @brand";
        command.Parameters.AddWithValue("@brand", _brand.BrandId);
        var products = new Dictionary<string, Guid>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) products[reader.GetString(0)] = reader.GetGuid(1);
        return products;
    }

    private async Task<string[]> AuditAsync()
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; SELECT Action FROM audit.Entries WHERE BrandId = @brand";
        command.Parameters.AddWithValue("@brand", _brand.BrandId);
        var actions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) actions.Add(reader.GetString(0));
        return [.. actions];
    }

    private async Task<Guid> SeedLaunchAsync(DateTimeOffset now)
    {
        var room = Guid.NewGuid();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var plan = new LaunchPlan(room, _brand.BrandId, "Monsoon runners", 1, 1, 30000m, "INR", "Asia/Kolkata", today.AddDays(-2), today.AddDays(12),
            ["en"], [new LaunchRegion("1007785", "Mumbai")], [new LaunchRegion("1007788", "Pune")], "1111111111", "555", null,
            [.. _products.Values.Select(id => id.ToString())], ["https://acme.example"],
            new LaunchSettings([new LaunchChannel("search", 10000m, "maximize_clicks"), new LaunchChannel("demand_gen", 10000m, "maximize_clicks"), new LaunchChannel("shopping", 10000m, "maximize_clicks")]), []);
        var ads = new (string Key, string Campaign, string Type, string Resource, string[] Products)[]
        {
            ("ad:runner", "campaign:search", "search", RunnerAd, ["RAIN-1"]),
            ("ad:pair", "campaign:search", "search", PairAd, ["RAIN-1", "RAIN-2"]),
            ("ad:school", "campaign:search", "search", SchoolAd, ["SCHOOL-1"]),
            ("ad:plain", "campaign:search", "search", PlainAd, []),
            ("ad:boot", "campaign:demand_gen", "demand_gen", BootAd, ["RAIN-2"]),
            ("ad:shopping", "campaign:shopping", "shopping", ShoppingAd, ["RAIN-1"]),
        };
        var resources = new Dictionary<string, string> { ["campaign:search"] = Search, ["campaign:demand_gen"] = DemandGen, ["campaign:shopping"] = Shopping, ["built"] = "true" };
        foreach (var ad in ads)
        {
            resources[ad.Key] = ad.Resource;
            _google.Resources[ad.Resource] = new JsonObject { ["resourceName"] = ad.Resource };
        }

        var journal = new BuildJournal([], [], [.. ads.Select(ad => new BuiltAd(ad.Key, ad.Campaign, ad.Type, [.. ad.Products.Select(offer => _products[offer].ToString())]))]);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        var json = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
        var launch = new LaunchRecord
        {
            Id = Guid.NewGuid(), RoomId = room, Plan = json, Hash = Kairo.Contracts.Approvals.ApprovalHash.Of(json), Status = "live", Step = "activate",
            Resources = JsonSerializer.Serialize(resources, JsonSerializerOptions.Web), CreatedAt = now, ActivatedAt = now,
            PendingBuild = JsonSerializer.Serialize(journal, JsonSerializerOptions.Web),
        };
        db.Launches.Add(launch);
        foreach (var ad in ads)
        foreach (var offer in ad.Products)
            db.ProductAds.Add(new ProductAdMap { Id = Guid.NewGuid(), RoomId = room, ProductId = _products[offer].ToString(), CampaignType = ad.Type, CampaignResource = resources[ad.Campaign], AdResource = ad.Resource });
        await db.SaveChangesAsync(Token);
        return room;
    }
}
