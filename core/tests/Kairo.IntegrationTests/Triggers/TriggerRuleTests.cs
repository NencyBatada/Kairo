using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.Triggers;

public sealed class TriggerRuleTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private RadarBrand _brand = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient Admin => _brand.Admin.Browser;

    private string Rules => $"{Triggered.Base(_brand.BrandId)}/rules";

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _brand = await Radar.BrandAsync(_api, connectAds: false);
    }

    [Fact]
    public async Task An_admin_creates_a_weather_rule_and_everyone_sees_it_in_plain_words()
    {
        var rule = await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.RainRule(["mumbai", "pune"]));

        Assert.Equal(("active", "manual", "weather"), (rule.GetProperty("status").GetString(), rule.GetProperty("origin").GetString(), rule.GetProperty("source").GetString()));
        Assert.Equal(
            "When heavy rain of 50 mm or more in a day is forecast within 3 days in Mumbai or Pune, suggest a campaign for Rain. Waits 3 days before firing again in the same region.",
            rule.GetProperty("description").GetString());
        Assert.Equal(["Mumbai", "Pune"], rule.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()));
        var listed = Assert.Single((await _brand.Marketer.Browser.GetFromJsonAsync<JsonElement[]>(Rules, Token))!);
        Assert.Equal(rule.GetProperty("id").GetGuid(), listed.GetProperty("id").GetGuid());
        Assert.Equal(0, listed.GetProperty("results").GetProperty("fired").GetInt32());
        Assert.Equal(["trigger_rule.created"], (await Workspaces.AuditActionsAsync(Admin, _brand.BrandId)).Take(1));
        Assert.Contains(await OutboxRows.ReadAsync(_database, _brand.BrandId), row => row.Type == "trigger_rules.changed");
    }

    [Fact]
    public async Task Only_admins_change_trigger_rules()
    {
        var marketer = _brand.Marketer.Browser;
        var rule = await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.RainRule(["mumbai"]));
        var id = rule.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PostAsJsonAsync(Rules, Triggered.RainRule(["mumbai"]), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PostAsJsonAsync($"{Rules}/preview", Triggered.RainRule(["mumbai"]), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PutAsJsonAsync($"{Rules}/{id}", Triggered.RainRule(["pune"]), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PostAsync($"{Rules}/{id}/pause", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.DeleteAsync($"{Rules}/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await marketer.GetAsync($"{Rules}/{id}", Token)).StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"x","source":"weather","condition":{"kind":"heavy_rain"},"regions":["mumbai"],"category":"Shoes"}""", "name")]
    [InlineData("""{"name":"Rain","source":"snow","regions":["mumbai"],"category":"Shoes"}""", "source")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"heavy_rain"},"regions":["maharashtra"],"category":"Shoes"}""", "regions")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"heavy_rain"},"regions":["atlantis"],"category":"Shoes"}""", "regions")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"heavy_rain","threshold":5},"regions":["mumbai"],"category":"Shoes"}""", "condition.threshold")]
    [InlineData("""{"name":"Heat","source":"weather","condition":{"kind":"heatwave","withinDays":9},"regions":["mumbai"],"category":"Shoes"}""", "condition.withinDays")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"fog"},"regions":["mumbai"],"category":"Shoes"}""", "condition.kind")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"heavy_rain"},"regions":["mumbai"],"category":""}""", "category")]
    [InlineData("""{"name":"Rain","source":"weather","condition":{"kind":"heavy_rain"},"regions":["mumbai"],"category":"Shoes","cooldownHours":0}""", "cooldownHours")]
    [InlineData("""{"name":"Diwali","source":"festival","condition":{"eventId":"7b0d1c55-2f4e-4c55-9a4c-6f1b0b7d2a11"},"category":"Shoes"}""", "condition.eventId")]
    [InlineData("""{"name":"Stock","source":"stock","condition":{},"category":"Shoes"}""", "condition.minUnits")]
    public async Task Rules_are_checked_before_they_are_saved(string body, string field)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await Admin.PostAsync(Rules, content, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await Catalogues.ErrorsAsync(response, field));
    }

    [Fact]
    public async Task A_preview_shows_the_rule_in_plain_words_without_saving_it()
    {
        var response = await Admin.PostAsJsonAsync($"{Rules}/preview", Triggered.StockRule(500, regions: ["kerala", "mumbai", "goa", "delhi"]), Token);

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "When Rain has 500 or more units in stock, suggest a campaign to clear them in Kerala, Mumbai, Goa and 1 more. Waits 3 days before firing again in the same region.",
            (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("description").GetString());
        Assert.Empty((await Admin.GetFromJsonAsync<JsonElement[]>(Rules, Token))!);
    }

    [Fact]
    public async Task A_festival_rule_names_its_event()
    {
        var festival = await Triggered.AddFestivalAsync(Admin, _brand.BrandId, "Diwali Sale", new DateOnly(2026, 11, 8), regions: ["delhi"]);

        var rule = await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.FestivalRule(festival, daysBefore: 10));

        Assert.Equal("Diwali Sale", rule.GetProperty("condition").GetProperty("eventName").GetString());
        Assert.StartsWith("When Diwali Sale is 10 days away, suggest a campaign for Rain in Delhi.", rule.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Rules_move_between_active_and_paused_and_can_be_deleted()
    {
        var id = (await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.RainRule(["mumbai"]))).GetProperty("id").GetGuid();

        var edited = await Admin.PutAsJsonAsync($"{Rules}/{id}", Triggered.RainRule(["chennai"], threshold: 80, cooldownHours: 30), Token);
        edited.EnsureSuccessStatusCode();
        var rule = (await edited.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("rule");
        Assert.Equal(80, rule.GetProperty("condition").GetProperty("threshold").GetDouble());
        Assert.EndsWith("Waits 30 hours before firing again in the same region.", rule.GetProperty("description").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Admin.PostAsync($"{Rules}/{id}/pause", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsync($"{Rules}/{id}/pause", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsync($"{Rules}/{id}/activate", null, Token)).StatusCode);
        Assert.Single((await Admin.GetFromJsonAsync<JsonElement[]>($"{Rules}?status=paused", Token))!);
        Assert.Equal(HttpStatusCode.OK, (await Admin.PostAsync($"{Rules}/{id}/resume", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"{Rules}/{id}", Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync($"{Rules}/{id}", Token)).StatusCode);
        Assert.Empty((await Admin.GetFromJsonAsync<JsonElement[]>(Rules, Token))!);
        Assert.Equal(
            ["trigger_rule.deleted", "trigger_rule.resumed", "trigger_rule.paused", "trigger_rule.updated", "trigger_rule.created"],
            (await Workspaces.AuditActionsAsync(Admin, _brand.BrandId)).Take(5));
    }

    [Fact]
    public async Task A_workspace_keeps_up_to_fifty_rules()
    {
        for (var i = 0; i < 50; i++)
        {
            await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.RainRule(["mumbai"], name: $"Rain {i}"));
        }

        var response = await Admin.PostAsJsonAsync(Rules, Triggered.RainRule(["mumbai"]), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_options_list_cities_states_and_in_stock_categories()
    {
        var options = await _brand.Marketer.Browser.GetFromJsonAsync<JsonElement>($"{Triggered.Base(_brand.BrandId)}/options", Token);

        var regions = options.GetProperty("regions").EnumerateArray().ToList();
        Assert.Contains(regions, region => region.GetProperty("id").GetString() == "mumbai" && region.GetProperty("kind").GetString() == "city" && region.GetProperty("stateId").GetString() == "maharashtra");
        Assert.Contains(regions, region => region.GetProperty("id").GetString() == "kerala" && region.GetProperty("kind").GetString() == "state");
        Assert.Equal(["Shoes > Rain", "Shoes > School"], options.GetProperty("categories").EnumerateArray().Select(category => category.GetString()));
        Assert.Equal(["heavy_rain", "heatwave"], options.GetProperty("weatherKinds").EnumerateArray().Select(kind => kind.GetProperty("kind").GetString()));
        Assert.Equal(72, options.GetProperty("defaultCooldownHours").GetInt32());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();
}
