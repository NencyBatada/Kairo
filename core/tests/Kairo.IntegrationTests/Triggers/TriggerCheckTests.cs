using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.Triggers;

public sealed class TriggerCheckTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;
    private readonly FakePlan _plan = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeWeather Weather => _api.GoogleApis.Weather;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Triggered.Morning);
        _plan.Attach(_worker.Agents);
    }

    [Fact]
    public async Task Heavy_rain_in_a_watched_city_creates_a_triggered_card_with_a_drafted_brief()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Rain["mumbai"] = [10, 62, 5];
        Weather.Rain["pune"] = [0, 20, 30];
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai", "pune"]));

        var run = await Triggered.CheckAsync(_worker);

        var result = Triggered.Result(run, brand.BrandId);
        Assert.Equal(("checked", 1, 1, 1), (result.GetProperty("outcome").GetString(), result.GetProperty("checked").GetInt32(), result.GetProperty("fired").GetInt32(), result.GetProperty("briefs").GetInt32()));
        Assert.Equal(["mumbai", "pune"], Weather.Calls.Order());
        Assert.All(Weather.Projects, project => Assert.Equal("kairo-test", project));
        var card = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal(("trigger", 0, true), (card.GetProperty("source").GetString(), card.GetProperty("score").GetInt32(), card.GetProperty("available").GetBoolean()));
        Assert.Equal("Heavy rain in Mumbai: Rain", card.GetProperty("topic").GetString());
        Assert.Equal(2, card.GetProperty("productCount").GetInt32());
        var trigger = card.GetProperty("trigger");
        Assert.Equal("weather", trigger.GetProperty("source").GetString());
        Assert.Equal("Monsoon rain", trigger.GetProperty("ruleName").GetString());
        Assert.Equal("Heavy rain is forecast in Mumbai (62 mm on 15 Oct).", trigger.GetProperty("summary").GetString());
        Assert.Equal(["Mumbai"], trigger.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 18, 30, 0, TimeSpan.Zero), trigger.GetProperty("conditionEndsAt").GetDateTimeOffset());

        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal("drafted", detail.GetProperty("briefStatus").GetString());
        var brief = detail.GetProperty("brief");
        Assert.Equal("Monsoon runners", brief.GetProperty("title").GetString());
        Assert.Equal(["Mumbai"], brief.GetProperty("regions").EnumerateArray().Select(region => region.GetString()));
        Assert.Single(brief.GetProperty("productIds").EnumerateArray());
        Assert.Equal("trigger", detail.GetProperty("reasonsStatus").GetString());
        var request = Assert.Single(_plan.Requests);
        Assert.Equal("Shoes > Rain", request.GetProperty("category").GetString());
        Assert.Equal("15 Oct", request.GetProperty("trigger").GetProperty("until").GetString());
        Assert.Equal(2, request.GetProperty("products").GetArrayLength());

        var alert = Assert.Single(await NotificationRows.NotificationsAsync(_database, brand.BrandId), row => row.Kind == "trigger.fired" && row.UserId == brand.Marketer.UserId);
        Assert.Equal("Trigger fired: Monsoon rain", alert.Title);
        Assert.Equal($"/{brand.Handle}/opportunities/{card.GetProperty("id").GetGuid()}", alert.Link);
        var entry = await Workspaces.LatestAuditEntryAsync(brand.Admin.Browser, brand.BrandId);
        Assert.Equal(("trigger.fired", "Sense agent"), (entry.GetProperty("action").GetString(), entry.GetProperty("actorName").GetString()));
        var progress = (await OutboxRows.ReadAsync(_database, brand.BrandId)).Where(row => row.Type == "agent.progress").Select(row => JsonDocument.Parse(row.Payload).RootElement).ToList();
        Assert.Equal(["running", "succeeded"], progress.Select(p => p.GetProperty("status").GetString()));
        Assert.All(progress, p => Assert.Equal("plan", p.GetProperty("agent").GetString()));

        var detailRule = await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid());
        Assert.Equal("fired", detailRule.GetProperty("rule").GetProperty("lastCheckOutcome").GetString());
        var results = detailRule.GetProperty("rule").GetProperty("results");
        Assert.Equal((1, 1, 0, 0), (results.GetProperty("fired").GetInt32(), results.GetProperty("created").GetInt32(), results.GetProperty("joined").GetInt32(), results.GetProperty("accepted").GetInt32()));
        Assert.Equal(JsonValueKind.Null, results.GetProperty("lift").ValueKind);
        var firing = Assert.Single(detailRule.GetProperty("firings").EnumerateArray());
        Assert.Equal(("created", "new"), (firing.GetProperty("outcome").GetString(), firing.GetProperty("opportunityStatus").GetString()));
    }

    [Fact]
    public async Task Weather_is_checked_hourly_and_forecasts_are_shared_across_brands_within_the_hour()
    {
        var first = await Radar.BrandAsync(_api, connectAds: false);
        var second = await Radar.BrandAsync(_api, connectAds: false);
        await Triggered.CreateRuleAsync(first.Admin.Browser, first.BrandId, Triggered.RainRule(["mumbai"]));
        await Triggered.CreateRuleAsync(second.Admin.Browser, second.BrandId, Triggered.RainRule(["mumbai"]));

        await Triggered.CheckAsync(_worker, first.BrandId);
        await Triggered.CheckAsync(_worker, second.BrandId);
        _worker.Clock.Advance(TimeSpan.FromMinutes(15));
        var soon = await Triggered.CheckAsync(_worker);
        _worker.Clock.Advance(TimeSpan.FromMinutes(45));
        await Triggered.CheckAsync(_worker, first.BrandId);
        await Triggered.CheckAsync(_worker, second.BrandId);

        Assert.Equal(0, Triggered.Result(soon, first.BrandId).GetProperty("checked").GetInt32());
        Assert.Equal(2, Weather.Calls.Count);
        Assert.Empty(await Radar.CardsAsync(first.Marketer.Browser, first.BrandId));
        var rule = (await first.Admin.Browser.GetFromJsonAsync<JsonElement[]>($"{Triggered.Base(first.BrandId)}/rules", Token))!.Single();
        Assert.Equal("quiet", rule.GetProperty("lastCheckOutcome").GetString());
        Assert.Equal(2, (await OutboxRows.ReadAsync(_database, first.BrandId)).Count(row => row.Type == "trigger_rules.changed"));
    }

    [Fact]
    public async Task A_rule_waits_out_its_cooldown_per_region_and_a_new_region_joins_the_open_card()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Rain["mumbai"] = [80, 80, 80];
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai", "pune"]));
        await Triggered.CheckAsync(_worker);

        _worker.Clock.Advance(TimeSpan.FromHours(1));
        await Triggered.CheckAsync(_worker);
        Assert.Equal("cooling_down", (await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid())).GetProperty("rule").GetProperty("lastCheckOutcome").GetString());

        Weather.Rain["pune"] = [0, 0, 70];
        _worker.Clock.Advance(TimeSpan.FromHours(1));
        await Triggered.CheckAsync(_worker);

        var card = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal(
            [["Pune"], ["Mumbai"]],
            detail.GetProperty("triggers").EnumerateArray().Select(trigger => trigger.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()).ToArray()));
        var firings = (await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid())).GetProperty("firings").EnumerateArray().ToList();
        Assert.Equal(["joined", "created"], firings.Select(firing => firing.GetProperty("outcome").GetString()));
        Assert.Equal(2, _plan.Drafts);
    }

    [Fact]
    public async Task Paused_rules_never_fire()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Rain["mumbai"] = [90];
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"]));
        (await brand.Admin.Browser.PostAsync($"{Triggered.Base(brand.BrandId)}/rules/{rule.GetProperty("id").GetGuid()}/pause", null, Token)).EnsureSuccessStatusCode();

        await Triggered.CheckAsync(_worker, force: true);

        Assert.Empty(Weather.Calls);
        Assert.Empty(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
    }

    [Fact]
    public async Task A_city_whose_forecast_fails_does_not_stop_other_brands()
    {
        var failing = await Radar.BrandAsync(_api, connectAds: false);
        var working = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Failing.Add("kochi");
        Weather.Rain["chennai"] = [120];
        var broken = await Triggered.CreateRuleAsync(failing.Admin.Browser, failing.BrandId, Triggered.RainRule(["kochi"]));
        await Triggered.CreateRuleAsync(working.Admin.Browser, working.BrandId, Triggered.RainRule(["chennai"]));

        var run = await Triggered.CheckAsync(_worker);

        Assert.Equal(0, Triggered.Result(run, failing.BrandId).GetProperty("fired").GetInt32());
        Assert.Equal(1, Triggered.Result(run, working.BrandId).GetProperty("fired").GetInt32());
        Assert.Equal("weather_unavailable", (await Triggered.RuleAsync(failing.Admin.Browser, failing.BrandId, broken.GetProperty("id").GetGuid())).GetProperty("rule").GetProperty("lastCheckOutcome").GetString());
    }

    [Fact]
    public async Task A_rule_with_nothing_in_stock_does_not_fire_and_does_not_start_its_cooldown()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false, catalogue: false);
        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, Radar.Catalogue("out_of_stock"));
        Weather.Rain["mumbai"] = [90, 90];
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"]));

        await Triggered.CheckAsync(_worker);
        Assert.Equal("no_stock", (await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid())).GetProperty("rule").GetProperty("lastCheckOutcome").GetString());

        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, Radar.Catalogue());
        _worker.Clock.Advance(TimeSpan.FromHours(1));
        await Triggered.CheckAsync(_worker);

        Assert.Single(await Triggered.TriggeredCardsAsync(brand.Marketer.Browser, brand.BrandId));
    }

    [Fact]
    public async Task A_festival_rule_fires_once_the_brand_local_date_reaches_its_window()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        var festival = await Triggered.AddFestivalAsync(brand.Admin.Browser, brand.BrandId, "Shoe Fest", new DateOnly(2026, 10, 21), regions: ["kerala"]);
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.FestivalRule(festival, daysBefore: 6));

        await Triggered.CheckAsync(_worker);
        _worker.Clock.Advance(TimeSpan.FromHours(10));
        await Triggered.CheckAsync(_worker);
        Assert.Empty(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));

        _worker.Clock.Advance(TimeSpan.FromHours(10));
        await Triggered.CheckAsync(_worker);

        var card = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal("Shoe Fest: Rain", card.GetProperty("topic").GetString());
        var trigger = card.GetProperty("trigger");
        Assert.Equal("Shoe Fest is 6 days away, on 21 Oct.", trigger.GetProperty("summary").GetString());
        Assert.Equal(["Kerala"], trigger.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 18, 30, 0, TimeSpan.Zero), trigger.GetProperty("conditionEndsAt").GetDateTimeOffset());
        Assert.Equal("fired", (await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid())).GetProperty("rule").GetProperty("lastCheckOutcome").GetString());
    }

    [Fact]
    public async Task A_stock_rule_checks_each_catalogue_update_and_its_card_expires_when_stock_falls()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        var rule = await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.StockRule(100));

        await Triggered.CheckAsync(_worker);
        Assert.Equal("needs_quantities", (await Triggered.RuleAsync(brand.Admin.Browser, brand.BrandId, rule.GetProperty("id").GetGuid())).GetProperty("rule").GetProperty("lastCheckOutcome").GetString());

        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, WithQuantities(80, 40));
        await Triggered.CheckAsync(_worker);
        var card = Assert.Single(await Triggered.TriggeredCardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal("Clear Rain stock", card.GetProperty("topic").GetString());
        Assert.Equal("Rain has 120 units in stock across 2 products.", card.GetProperty("trigger").GetProperty("summary").GetString());
        Assert.Equal(["India"], card.GetProperty("trigger").GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()));

        _worker.Clock.Advance(TimeSpan.FromMinutes(15));
        await Triggered.CheckAsync(_worker);
        Assert.Single(await Triggered.TriggeredCardsAsync(brand.Marketer.Browser, brand.BrandId));

        await Catalogues.ImportAsync(brand.Admin.Browser, brand.BrandId, WithQuantities(10, 5));
        await Triggered.CheckAsync(_worker);

        Assert.Empty(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        var expired = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?status=expired"));
        Assert.True(expired.GetProperty("trigger").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_trigger_joins_an_open_radar_card_that_sells_the_same_products()
    {
        var sense = new FakeSense();
        sense.Attach(_worker.Agents);
        _plan.Attach(_worker.Agents);
        Radar.PlanDemand(_api.GoogleApis.Keywords);
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker, brand.BrandId);
        Weather.Rain["mumbai"] = [70];
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"]));

        await Triggered.CheckAsync(_worker);

        var card = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal(("Monsoon footwear", "radar"), (card.GetProperty("topic").GetString(), card.GetProperty("source").GetString()));
        Assert.Equal("Monsoon rain", card.GetProperty("trigger").GetProperty("ruleName").GetString());
        var detail = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal("drafted", detail.GetProperty("briefStatus").GetString());

        _worker.Clock.Advance(TimeSpan.FromDays(2));
        await Triggered.CheckAsync(_worker);

        var after = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal("new", after.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("trigger").ValueKind);
    }

    [Fact]
    public async Task Triggered_cards_sort_above_radar_cards_and_can_be_filtered()
    {
        new FakeSense().Attach(_worker.Agents);
        _plan.Attach(_worker.Agents);
        Radar.PlanDemand(_api.GoogleApis.Keywords);
        var brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker, brand.BrandId);
        Weather.Rain["chennai"] = [70];
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["chennai"], category: "Shoes > School", name: "School rain"));

        await Triggered.CheckAsync(_worker);

        var cards = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId);
        Assert.Equal(["trigger", "radar"], cards.Select(card => card.GetProperty("source").GetString()));
        Assert.Equal("radar", Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?triggered=false")).GetProperty("source").GetString());
        Assert.Equal("trigger", Assert.Single(await Triggered.TriggeredCardsAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("source").GetString());
    }

    [Fact]
    public async Task Radar_refreshes_do_not_expire_triggered_cards()
    {
        var sense = new FakeSense();
        sense.Topics.Clear();
        sense.Attach(_worker.Agents);
        _plan.Attach(_worker.Agents);
        Radar.PlanDemand(_api.GoogleApis.Keywords);
        var brand = await Radar.BrandAsync(_api);
        Weather.Rain["mumbai"] = [0, 0, 0, 0, 0, 0, 90];
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"], withinDays: 7));
        await Triggered.CheckAsync(_worker);

        for (var day = 0; day < 3; day++)
        {
            await Radar.RefreshAsync(_worker, brand.BrandId);
            _worker.Clock.Advance(TimeSpan.FromHours(1));
        }

        Assert.Equal("new", Assert.Single(await Triggered.TriggeredCardsAsync(brand.Marketer.Browser, brand.BrandId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_failed_brief_is_retried_on_the_next_check()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Rain["mumbai"] = [90, 90];
        _plan.Fails = true;
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"]));
        await Triggered.CheckAsync(_worker);
        var card = Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        var failed = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal(("failed", JsonValueKind.Null), (failed.GetProperty("briefStatus").GetString(), failed.GetProperty("brief").ValueKind));

        _plan.Fails = false;
        _worker.Clock.Advance(TimeSpan.FromMinutes(15));
        var run = await Triggered.CheckAsync(_worker);

        Assert.Equal(1, Triggered.Result(run, brand.BrandId).GetProperty("briefs").GetInt32());
        var drafted = await Radar.DetailAsync(brand.Marketer.Browser, brand.BrandId, card.GetProperty("id").GetGuid());
        Assert.Equal("drafted", drafted.GetProperty("briefStatus").GetString());
    }

    [Fact]
    public async Task A_triggered_card_expires_when_its_condition_ends_unless_someone_accepted_it()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false);
        Weather.Rain["mumbai"] = [90];
        Weather.Rain["chennai"] = [90];
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["mumbai"], category: Radar.RainCategory));
        await Triggered.CreateRuleAsync(brand.Admin.Browser, brand.BrandId, Triggered.RainRule(["chennai"], category: "Shoes > School", name: "School rain"));
        await Triggered.CheckAsync(_worker);
        var cards = await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId);
        Assert.Equal(2, cards.Length);
        var school = cards.Single(card => card.GetProperty("topic").GetString()!.EndsWith("School", StringComparison.Ordinal));
        var accepted = await Radar.DecideAsync(brand.Marketer.Browser, brand.BrandId, school.GetProperty("id").GetGuid(), "accept");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var evidence = (await accepted.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("opportunity");
        Assert.Equal("drafted", evidence.GetProperty("briefStatus").GetString());
        Assert.Equal("School rain", Assert.Single(evidence.GetProperty("triggers").EnumerateArray()).GetProperty("ruleName").GetString());

        Weather.Rain.Clear();
        _worker.Clock.Advance(TimeSpan.FromDays(1));
        var run = await Triggered.CheckAsync(_worker);

        Assert.Equal(2, Triggered.Result(run, brand.BrandId).GetProperty("ended").GetInt32());
        Assert.Empty(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId));
        Assert.Equal("Heavy rain in Mumbai: Rain", Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?status=expired")).GetProperty("topic").GetString());
        Assert.Single(await Radar.CardsAsync(brand.Marketer.Browser, brand.BrandId, "?status=accepted"));
        var rules = (await brand.Admin.Browser.GetFromJsonAsync<JsonElement[]>($"{Triggered.Base(brand.BrandId)}/rules", Token))!;
        Assert.Equal(1, rules.Single(rule => rule.GetProperty("name").GetString() == "School rain").GetProperty("results").GetProperty("accepted").GetInt32());
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static string WithQuantities(int first, int second) =>
        "id,title,link,image_link,price,availability,product_type,quantity\n"
        + $"RAIN-1,Monsoon Waterproof Runner,https://acme.example/p/1,https://acme.example/i/1.jpg,1499.00 INR,in_stock,{Radar.RainCategory},{first}\n"
        + $"RAIN-2,Rain Boots Classic,https://acme.example/p/2,https://acme.example/i/2.jpg,1499.00 INR,in_stock,{Radar.RainCategory},{second}\n"
        + "SCHOOL-1,School Shoes Black,https://acme.example/p/3,https://acme.example/i/3.jpg,1499.00 INR,in_stock,Shoes > School,500\n";
}
