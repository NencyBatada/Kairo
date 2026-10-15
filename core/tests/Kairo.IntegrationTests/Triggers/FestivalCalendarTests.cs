using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Opportunities;
using Kairo.Modules.Triggers.Festivals;

namespace Kairo.IntegrationTests.Triggers;

public sealed class FestivalCalendarTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private RadarBrand _brand = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient Admin => _brand.Admin.Browser;

    private string Festivals => $"{Triggered.Base(_brand.BrandId)}/festivals";

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _brand = await Radar.BrandAsync(_api, connectAds: false, catalogue: false);
    }

    [Fact]
    public async Task A_brand_calendar_starts_with_kairos_india_festivals_once()
    {
        var today = DateOnly.FromDateTime(_api.Clock.GetUtcNow().UtcDateTime);
        var expected = IndiaFestivals.All.Where(festival => festival.EndsOn >= today).OrderBy(festival => festival.StartsOn).ThenBy(festival => festival.Name, StringComparer.Ordinal).ToList();

        var seeded = await ListAsync(_brand.Marketer.Browser);

        Assert.Equal(expected.Select(festival => (festival.Name, festival.StartsOn)), seeded.Select(festival => (festival.GetProperty("name").GetString()!, DateOnly.Parse(festival.GetProperty("startsOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture))));
        Assert.All(seeded, festival => Assert.Equal("kairo", festival.GetProperty("origin").GetString()));
        var regional = expected.First(festival => festival.Regions.Count > 0);
        var listed = seeded.First(festival => festival.GetProperty("name").GetString() == regional.Name);
        Assert.Equal(regional.Regions, listed.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("id").GetString()!));
        var first = seeded[0];
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"{Festivals}/{first.GetProperty("id").GetGuid()}", Token)).StatusCode);

        var again = await ListAsync(Admin);

        Assert.Equal(seeded.Length - 1, again.Length);
        Assert.DoesNotContain(again, festival => festival.GetProperty("id").GetGuid() == first.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Admins_add_change_and_remove_their_own_events()
    {
        var id = await Triggered.AddFestivalAsync(Admin, _brand.BrandId, "  Founders   Day ", new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 3), ["karnataka", "india"]);

        var added = (await ListAsync(Admin, "?from=2027-02-01&to=2027-02-01")).Single(festival => festival.GetProperty("id").GetGuid() == id);
        Assert.Equal(("Founders Day", "brand", "2027-02-03"), (added.GetProperty("name").GetString(), added.GetProperty("origin").GetString(), added.GetProperty("endsOn").GetString()));
        Assert.Equal(["karnataka"], added.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("id").GetString()));

        var changed = await Admin.PutAsJsonAsync($"{Festivals}/{id}", new { name = "Founders Week", startsOn = "2027-02-01", endsOn = "2027-02-07", regions = Array.Empty<string>() }, Token);
        changed.EnsureSuccessStatusCode();
        Assert.Equal("Founders Week", (await changed.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"{Festivals}/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.DeleteAsync($"{Festivals}/{id}", Token)).StatusCode);
        Assert.Equal(["festival.removed", "festival.updated", "festival.added"], (await Workspaces.AuditActionsAsync(Admin, _brand.BrandId)).Take(3));
        Assert.Contains(await OutboxRows.ReadAsync(_database, _brand.BrandId), row => row.Type == "festivals.changed");
    }

    [Fact]
    public async Task Marketers_can_read_but_not_change_the_calendar()
    {
        var response = await _brand.Marketer.Browser.PostAsJsonAsync(Festivals, new { name = "Sale", startsOn = "2027-01-05" }, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEmpty(await ListAsync(_brand.Marketer.Browser));
    }

    [Theory]
    [InlineData("""{"name":"S","startsOn":"2027-01-05"}""", "name")]
    [InlineData("""{"name":"Sale"}""", "startsOn")]
    [InlineData("""{"name":"Sale","startsOn":"2027-01-05","endsOn":"2027-01-04"}""", "endsOn")]
    [InlineData("""{"name":"Sale","startsOn":"2027-01-05","endsOn":"2027-01-19"}""", "endsOn")]
    [InlineData("""{"name":"Sale","startsOn":"2027-01-05","regions":["mumbai"]}""", "regions")]
    public async Task Events_are_checked_before_they_are_saved(string body, string field)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await Admin.PostAsync(Festivals, content, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await Catalogues.ErrorsAsync(response, field));
    }

    [Fact]
    public async Task An_event_a_rule_uses_cannot_be_removed()
    {
        var id = await Triggered.AddFestivalAsync(Admin, _brand.BrandId, "Monsoon Sale", new DateOnly(2027, 6, 1));
        await Triggered.CreateRuleAsync(Admin, _brand.BrandId, Triggered.FestivalRule(id));

        var response = await Admin.DeleteAsync($"{Festivals}/{id}", Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var festival = (await ListAsync(Admin, "?from=2027-06-01&to=2027-06-01")).Single(f => f.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, festival.GetProperty("rules").GetInt32());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<JsonElement[]> ListAsync(HttpClient browser, string query = "") =>
        (await browser.GetFromJsonAsync<JsonElement[]>($"{Festivals}{query}", Token))!;
}
