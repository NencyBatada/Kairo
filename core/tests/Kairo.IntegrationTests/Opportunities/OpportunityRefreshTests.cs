using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Opportunities.Refresh;
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Opportunities;

public sealed class OpportunityRefreshTests(SqlServer sqlServer) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 0, 30, 0, TimeSpan.Zero);

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;
    private readonly FakeSense _sense = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeKeywordPlanner Planner => _api.GoogleApis.Keywords;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Morning);
        _sense.Attach(_worker.Agents);
        Radar.PlanDemand(Planner);
    }

    [Fact]
    public async Task A_refresh_ranks_rising_searches_that_match_products_in_stock()
    {
        var brand = await Radar.BrandAsync(_api);

        var run = await Radar.RefreshAsync(_worker);

        var result = Radar.Result(run, brand.BrandId);
        Assert.Equal("succeeded", result.GetProperty("outcome").GetString());
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var list = await Radar.ListAsync(brand.Marketer.Browser, brand.BrandId);
        Assert.Equal("succeeded", list.GetProperty("lastOutcome").GetString());
        var card = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Monsoon footwear", card.GetProperty("topic").GetString());
        Assert.Equal("new", card.GetProperty("status").GetString());
        Assert.Equal("radar", card.GetProperty("source").GetString());
        Assert.InRange(card.GetProperty("score").GetInt32(), 40, 100);
        Assert.Equal(7000, card.GetProperty("lastMonthSearches").GetInt64());
        Assert.Equal(12, card.GetProperty("trend").GetArrayLength());
        Assert.True(card.GetProperty("available").GetBoolean());
        Assert.False(card.GetProperty("watched").GetBoolean());
        Assert.Equal(2, card.GetProperty("productCount").GetInt32());
        Assert.Contains("Monsoon footwear", card.GetProperty("reason").GetString());
        Assert.Equal("Kerala", Assert.Single(card.GetProperty("regions").EnumerateArray()).GetProperty("name").GetString());

        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal(24, detail.GetProperty("trend").GetArrayLength());
        Assert.Equal(3, detail.GetProperty("regions").GetArrayLength());
        Assert.Equal("sourced", detail.GetProperty("reasonsStatus").GetString());
        var source = Assert.Single(Assert.Single(detail.GetProperty("reasons").EnumerateArray()).GetProperty("sources").EnumerateArray());
        Assert.Equal("https://weather.example/monsoon", source.GetProperty("url").GetString());
        Assert.Equal(["rain boots", "waterproof shoes"], detail.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()).Order());
        var breakdown = detail.GetProperty("scoreBreakdown");
        Assert.Equal(0.9, breakdown.GetProperty("fit").GetDouble());
        Assert.Equal(90, breakdown.GetProperty("fitScore").GetInt32());
        Assert.Equal(1.0, breakdown.GetProperty("feedback").GetDouble());
        Assert.Equal(card.GetProperty("score").GetInt32(), breakdown.GetProperty("total").GetInt32());
        Assert.Equal("gemini-test", detail.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Sense_gets_only_rising_keywords_and_in_stock_categories()
    {
        var brand = await Radar.BrandAsync(_api);
        await BrandKitsApproveAsync(brand, "Rain shoes");

        await Radar.RefreshAsync(_worker);

        Assert.Contains("Rain shoes", Planner.SeedsSent());
        Assert.Contains("Rain", Planner.SeedsSent());
        var match = _worker.Agents.Requests.Single(request => request.Url.EndsWith("/sense/match", StringComparison.Ordinal)).Body;
        Assert.Equal(["rain boots", "waterproof shoes"], match.GetProperty("keywords").EnumerateArray().Select(k => k.GetProperty("text").GetString()).Order());
        Assert.Equal(["Shoes > Rain", "Shoes > School"], match.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Order());
        Assert.Equal("India", match.GetProperty("market").GetProperty("country").GetString());
        var explain = _worker.Agents.Requests.Single(request => request.Url.EndsWith("/sense/explain", StringComparison.Ordinal)).Body;
        Assert.Equal("t1", Assert.Single(explain.GetProperty("topics").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task The_refresh_is_recorded_and_marketers_hear_about_new_opportunities()
    {
        var brand = await Radar.BrandAsync(_api);

        await Radar.RefreshAsync(_worker);

        var entry = await Workspaces.LatestAuditEntryAsync(brand.Admin.Browser, brand.BrandId);
        Assert.Equal("opportunities.refreshed", entry.GetProperty("action").GetString());
        Assert.Equal("agent", entry.GetProperty("actorType").GetString());
        Assert.Equal("Sense agent", entry.GetProperty("actorName").GetString());
        var alerts = (await NotificationRows.NotificationsAsync(_database, brand.BrandId)).Where(row => row.Kind == "opportunities.ready").ToList();
        Assert.Equal([brand.Admin.UserId, brand.Marketer.UserId], alerts.Select(row => row.UserId).Order());
        var alert = alerts[0];
        Assert.True(alert.AwaitsDigest);
        Assert.Equal("1 new opportunity", alert.Title);
        Assert.Equal($"/{brand.Handle}/opportunities", alert.Link);
        var progress = (await OutboxRows.ReadAsync(_database, brand.BrandId))
            .Where(row => row.Type == "agent.progress")
            .Select(row => JsonDocument.Parse(row.Payload).RootElement)
            .ToList();
        Assert.Equal(["running", "running", "running", "running", "succeeded"], progress.Select(p => p.GetProperty("status").GetString()));
        Assert.All(progress, p => Assert.Equal("sense", p.GetProperty("agent").GetString()));
    }

    [Fact]
    public async Task Brands_refresh_once_a_day_from_five_in_the_morning_local_time()
    {
        var brand = await Radar.BrandAsync(_api);

        var first = await Radar.RefreshAsync(_worker);
        _worker.Clock.Advance(TimeSpan.FromHours(1));
        var sameDay = await Radar.RefreshAsync(_worker);
        _worker.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 14, 23, 0, 0, TimeSpan.Zero));
        var beforeFive = await Radar.RefreshAsync(_worker);
        _worker.Clock.Advance(TimeSpan.FromHours(1));
        var afterFive = await Radar.RefreshAsync(_worker);

        Assert.Equal("succeeded", Radar.Outcome(first, brand.BrandId));
        Assert.Equal(0, sameDay.GetProperty("brands").GetInt32());
        Assert.Equal(0, beforeFive.GetProperty("brands").GetInt32());
        Assert.Equal("succeeded", Radar.Outcome(afterFive, brand.BrandId));
    }

    [Fact]
    public async Task A_run_for_one_brand_refreshes_it_even_when_it_already_ran_today()
    {
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker);

        var again = await Radar.RefreshAsync(_worker, brand.BrandId);

        Assert.Equal(1, again.GetProperty("brands").GetInt32());
        var result = Radar.Result(again, brand.BrandId);
        Assert.Equal(0, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
    }

    [Fact]
    public async Task Brands_without_Google_Ads_or_stock_are_skipped()
    {
        var noAds = await Radar.BrandAsync(_api, connectAds: false);
        var noStock = await Radar.BrandAsync(_api, catalogue: false);

        var run = await Radar.RefreshAsync(_worker);

        Assert.Equal(("skipped", "not_connected"), Skip(run, noAds.BrandId));
        Assert.Equal(("skipped", "no_stock"), Skip(run, noStock.BrandId));
        Assert.Empty(_worker.Agents.Requests);
        foreach (var brandId in new[] { noAds.BrandId, noStock.BrandId })
        {
            Assert.DoesNotContain(await OutboxRows.ReadAsync(_database, brandId), row => row.Type == "agent.progress");
        }
    }

    [Fact]
    public async Task A_brand_already_refreshing_is_skipped()
    {
        var brand = await Radar.BrandAsync(_api);

        JsonElement run;
        await using (await HoldAsync(brand.BrandId))
        {
            run = await Radar.RefreshAsync(_worker);
        }

        Assert.Equal(("skipped", "busy"), Skip(run, brand.BrandId));
    }

    [Fact]
    public async Task Lost_Google_Ads_access_fails_the_refresh_and_asks_for_a_reconnect()
    {
        var brand = await Radar.BrandAsync(_api);
        Planner.AccessError = "USER_PERMISSION_DENIED";

        var run = await Radar.RefreshAsync(_worker);

        Assert.Equal(("failed", "access_lost"), Skip(run, brand.BrandId));
        var connection = await Connections.GetAsync(brand.Admin.Browser, brand.BrandId, "google-ads");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_failed_match_fails_the_refresh_and_a_failed_explanation_keeps_the_cards_without_reasons()
    {
        var failing = await Radar.BrandAsync(_api);
        _sense.MatchFails = true;
        var failed = await Radar.RefreshAsync(_worker, failing.BrandId);
        _sense.MatchFails = false;
        _sense.ExplainFails = true;

        var explained = await Radar.RefreshAsync(_worker, failing.BrandId);

        Assert.Equal(("failed", "agent"), Skip(failed, failing.BrandId));
        Assert.Equal("succeeded", Radar.Outcome(explained, failing.BrandId));
        var card = await Radar.OnlyCardAsync(failing.Marketer.Browser, failing.BrandId);
        var detail = await Radar.DetailAsync(failing.Marketer.Browser, failing.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal("unavailable", detail.GetProperty("reasonsStatus").GetString());
        Assert.Empty(detail.GetProperty("reasons").EnumerateArray());
        Assert.Equal("Waterproof runners are in stock.", card.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Reasons_without_a_source_are_dropped_and_the_card_says_so()
    {
        var brand = await Radar.BrandAsync(_api);
        _sense.Reasons = _ => [new { text = "People like boots.", sources = Array.Empty<object>() }, new { text = "Bad link.", sources = new[] { new { title = "x", url = "javascript:alert(1)" } } }];

        await Radar.RefreshAsync(_worker);

        var card = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId);
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal("unsourced", detail.GetProperty("reasonsStatus").GetString());
        Assert.Empty(detail.GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task A_region_without_data_doesnt_stop_the_refresh()
    {
        var brand = await Radar.BrandAsync(_api);
        Planner.FailingRegions.Add(FakeKeywordPlanner.Maharashtra);

        var run = await Radar.RefreshAsync(_worker);

        Assert.Equal("succeeded", Radar.Outcome(run, brand.BrandId));
        var card = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId);
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal(["Karnataka", "Kerala"], detail.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Order());
    }

    [Fact]
    public async Task Topics_with_no_product_in_stock_are_not_shown()
    {
        var brand = await Radar.BrandAsync(_api);
        _sense.Topics.Add(new { name = "Football boots", keywords = new[] { "football boots" }, categories = Array.Empty<string>(), fit = 0.8, fitReason = "None." });
        Planner.Volumes["football boots"] = FakeKeywordPlanner.Rising(1000, 9000);

        var run = await Radar.RefreshAsync(_worker);

        Assert.Equal(1, Radar.Result(run, brand.BrandId).GetProperty("created").GetInt32());
        Assert.Equal("Monsoon footwear", (await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("topic").GetString());
    }

    [Fact]
    public async Task An_opportunity_hides_while_its_products_are_out_of_stock_and_returns_with_them()
    {
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker);
        var id = (await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("id").GetGuid();

        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, Radar.Catalogue("out_of_stock"));
        var hidden = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId);
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, id);
        var accept = await Radar.DecideAsync(brand.Marketer.Browser, brand.BrandId, id, "accept");
        var refreshed = await Radar.RefreshAsync(_worker, brand.BrandId);
        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, Radar.Catalogue());
        var back = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId);

        Assert.Empty(hidden);
        Assert.False(detail.GetProperty("available").GetBoolean());
        Assert.Empty(detail.GetProperty("products").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal(1, Radar.Result(refreshed, brand.BrandId).GetProperty("updated").GetInt32());
        Assert.Equal(id, back.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task An_opportunity_that_stops_rising_expires_after_three_refreshes()
    {
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker);
        var id = (await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("id").GetGuid();
        (await Radar.DecideAsync(brand.Marketer.Browser, brand.BrandId, id, "save")).EnsureSuccessStatusCode();
        Planner.Volumes["rain boots"] = FakeKeywordPlanner.Flat(1000);
        Planner.Volumes["waterproof shoes"] = FakeKeywordPlanner.Flat(2000);

        await Radar.RefreshAsync(_worker, brand.BrandId);
        await Radar.RefreshAsync(_worker, brand.BrandId);
        var stillSaved = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?status=saved");
        var third = await Radar.RefreshAsync(_worker, brand.BrandId);

        Assert.Single(stillSaved);
        Assert.Equal(1, Radar.Result(third, brand.BrandId).GetProperty("expired").GetInt32());
        Assert.Empty(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal(id, (await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId, "?status=expired")).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_dismissed_opportunity_returns_only_when_demand_rises_sharply_again()
    {
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker);
        var id = (await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("id").GetGuid();
        (await Radar.DecideAsync(brand.Marketer.Browser, brand.BrandId, id, "dismiss", new { reason = "wrong_timing" })).EnsureSuccessStatusCode();

        var same = await Radar.RefreshAsync(_worker, brand.BrandId);
        var whileQuiet = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId);
        Planner.Volumes["rain boots"] = FakeKeywordPlanner.Rising(1000, 9000);
        Planner.Volumes["waterproof shoes"] = FakeKeywordPlanner.Rising(2000, 18000);
        var sharp = await Radar.RefreshAsync(_worker, brand.BrandId);

        Assert.Equal(0, Radar.Result(same, brand.BrandId).GetProperty("created").GetInt32());
        Assert.Empty(whileQuiet);
        Assert.Equal(1, Radar.Result(sharp, brand.BrandId).GetProperty("created").GetInt32());
        var back = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId);
        Assert.NotEqual(id, back.GetProperty("id").GetGuid());
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, back.GetProperty("id").GetGuid());
        Assert.Equal(0.9, detail.GetProperty("scoreBreakdown").GetProperty("feedback").GetDouble());
        Assert.Equal(["wrong_timing"], detail.GetProperty("scoreBreakdown").GetProperty("feedbackReasons").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Ignored_topics_never_reach_Sense_or_the_list()
    {
        var brand = await Radar.BrandAsync(_api);
        (await Radar.AddTopicAsync(brand.Marketer.Browser, brand.BrandId, "Waterproof", "ignore")).EnsureSuccessStatusCode();

        await Radar.RefreshAsync(_worker);
        var card = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId);
        (await Radar.AddTopicAsync(brand.Marketer.Browser, brand.BrandId, "monsoon", "ignore")).EnsureSuccessStatusCode();
        var afterIgnoring = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId);

        var match = _worker.Agents.Requests.Single(request => request.Url.EndsWith("/sense/match", StringComparison.Ordinal)).Body;
        Assert.Equal(["rain boots"], match.GetProperty("keywords").EnumerateArray().Select(k => k.GetProperty("text").GetString()));
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal(["rain boots"], detail.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()));
        Assert.Empty(afterIgnoring);
    }

    [Fact]
    public async Task Watched_topics_seed_the_search_and_show_below_the_cut_off()
    {
        var brand = await Radar.BrandAsync(_api);
        (await Radar.AddTopicAsync(brand.Marketer.Browser, brand.BrandId, "school shoes", "watch")).EnsureSuccessStatusCode();
        _sense.Topics.Add(new { name = "School shoes", keywords = new[] { "school shoes" }, categories = new[] { "Shoes > School" }, fit = 0.1, fitReason = "One school shoe." });

        await Radar.RefreshAsync(_worker);

        Assert.Equal("school shoes", Planner.SeedsSent()[0]);
        var match = _worker.Agents.Requests.Single(request => request.Url.EndsWith("/sense/match", StringComparison.Ordinal)).Body;
        Assert.Contains("school shoes", match.GetProperty("keywords").EnumerateArray().Select(k => k.GetProperty("text").GetString()));
        Assert.Equal(["school shoes"], match.GetProperty("watched").EnumerateArray().Select(w => w.GetString()));
        var watched = await Radar.OnlyCardAsync(brand.Marketer.Browser, brand.BrandId, "?watched=true");
        Assert.Equal("School shoes", watched.GetProperty("topic").GetString());
        Assert.InRange(watched.GetProperty("score").GetInt32(), 0, 39);
        Assert.Equal(2, (await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId)).Length);
        Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?minScore=40"));
    }

    [Fact]
    public async Task One_brands_failure_doesnt_stop_the_others()
    {
        var failing = await Radar.BrandAsync(_api, account: "3333333333");
        var working = await Radar.BrandAsync(_api);
        Planner.FailingCustomers.Add("3333333333");

        var run = await Radar.RefreshAsync(_worker);

        Assert.Equal("failed", Radar.Outcome(run, failing.BrandId));
        Assert.Equal("succeeded", Radar.Outcome(run, working.BrandId));
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static (string, string?) Skip(JsonElement run, Guid brandId)
    {
        var result = Radar.Result(run, brandId);
        return (result.GetProperty("outcome").GetString()!, result.GetProperty("reason").GetString());
    }

    private async Task<IAsyncDisposable> HoldAsync(Guid brandId)
    {
        var database = new KairoDatabase(_database);
        await database.BeginTransactionAsync(Token);
        Assert.True(await database.TryLockAsync(BrandRefresher.LockName(brandId), Token));
        return database;
    }

    private static async Task BrandKitsApproveAsync(RadarBrand brand, string category) =>
        await BrandKit.BrandKits.ApproveNewAsync(brand.Admin.Browser, brand.BrandId, new { productCategories = new[] { new { name = category } } });
}
