using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.Opportunities;

public sealed class WatchTopicTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private RadarBrand _brand = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Topics => $"{Radar.Base(_brand.BrandId)}/topics";

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _brand = await Radar.BrandAsync(_api, connectAds: false, catalogue: false);
    }

    [Fact]
    public async Task Marketers_and_admins_add_watched_and_ignored_topics()
    {
        var watch = await Radar.AddTopicAsync(_brand.Marketer.Browser, _brand.BrandId, "  monsoon   shoes ", "watch");
        var ignore = await Radar.AddTopicAsync(_brand.Admin.Browser, _brand.BrandId, "football", "ignore");

        Assert.Equal(HttpStatusCode.Created, watch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ignore.StatusCode);
        var created = await watch.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal($"{Topics}/{created.GetProperty("id").GetGuid()}", watch.Headers.Location!.OriginalString);
        var list = await _brand.Marketer.Browser.GetFromJsonAsync<JsonElement>(Topics, Token);
        Assert.Equal(
            [("ignore", "football"), ("watch", "monsoon shoes")],
            list.EnumerateArray().Select(topic => (topic.GetProperty("kind").GetString(), topic.GetProperty("phrase").GetString())));
        Assert.Equal("opportunity_topic.added", (await Workspaces.LatestAuditEntryAsync(_brand.Admin.Browser, _brand.BrandId)).GetProperty("action").GetString());
        Assert.Equal(2, (await OutboxRows.ReadAsync(_database, _brand.BrandId)).Count(row => row.Type == "opportunity_topics.changed"));
    }

    [Fact]
    public async Task A_topic_can_be_on_the_list_only_once_whatever_its_case()
    {
        (await Radar.AddTopicAsync(_brand.Marketer.Browser, _brand.BrandId, "Rain boots", "watch")).EnsureSuccessStatusCode();

        var duplicate = await Radar.AddTopicAsync(_brand.Marketer.Browser, _brand.BrandId, "rain BOOTS", "ignore");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("That topic is already on your list.", (await duplicate.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("x", "watch", "phrase")]
    [InlineData("rain", "follow", "kind")]
    [InlineData("rain", null, "kind")]
    public async Task Bad_topics_are_rejected(string phrase, string? kind, string field)
    {
        var response = await _brand.Marketer.Browser.PostAsJsonAsync(Topics, new { phrase, kind }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task A_topic_can_be_removed()
    {
        var created = await (await Radar.AddTopicAsync(_brand.Marketer.Browser, _brand.BrandId, "rain", "watch")).Content.ReadFromJsonAsync<JsonElement>(Token);
        var id = created.GetProperty("id").GetGuid();

        var removed = await _brand.Marketer.Browser.DeleteAsync($"{Topics}/{id}", Token);
        var again = await _brand.Marketer.Browser.DeleteAsync($"{Topics}/{id}", Token);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty((await _brand.Marketer.Browser.GetFromJsonAsync<JsonElement>(Topics, Token)).EnumerateArray());
        Assert.Equal("opportunity_topic.removed", (await Workspaces.LatestAuditEntryAsync(_brand.Admin.Browser, _brand.BrandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Approvers_can_see_topics_but_not_change_them()
    {
        var approver = await Workspaces.JoinAsync(_api, _brand.Admin.Browser, _brand.BrandId, "approver");
        (await Radar.AddTopicAsync(_brand.Marketer.Browser, _brand.BrandId, "rain", "watch")).EnsureSuccessStatusCode();

        var add = await Radar.AddTopicAsync(approver.Browser, _brand.BrandId, "snow", "watch");
        var list = await approver.Browser.GetFromJsonAsync<JsonElement>(Topics, Token);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Single(list.EnumerateArray());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();
}
