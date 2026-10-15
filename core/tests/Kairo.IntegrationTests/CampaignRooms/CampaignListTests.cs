using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CampaignListTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private Guid _monsoon;
    private Guid _school;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer, (sense, planner) =>
        {
            sense.Topics.Add(new { name = "Back to school shoes", keywords = new[] { "school shoes" }, categories = new[] { "Shoes > School" }, fit = 0.8, fitReason = "School shoes are in stock." });
            planner.Volumes["school shoes"] = FakeKeywordPlanner.Rising(1000, 5000);
        });
        var cards = await Radar.CardsAsync(_world.Marketer, _world.BrandId);
        var ids = cards.ToDictionary(card => card.GetProperty("topic").GetString()!, card => card.GetProperty("id").GetGuid());
        _monsoon = await _world.AcceptAsync(ids["Monsoon footwear"]);
        _world.Api.Clock.Advance(TimeSpan.FromDays(2));
        _school = await _world.AcceptAsync(ids["Back to school shoes"]);
        await _world.RunStagesAsync();
    }

    [Fact]
    public async Task The_list_shows_newest_first_with_owner_stage_and_products()
    {
        var cards = await Campaigns.CardsAsync(_world.Approver.Browser, _world.BrandId);

        Assert.Equal([_school, _monsoon], cards.Select(card => card.GetProperty("id").GetGuid()));
        var monsoon = cards[1];
        Assert.Equal(("Monsoon footwear", "brief_draft", "in_progress"), (monsoon.GetProperty("title").GetString(), monsoon.GetProperty("stage").GetString(), monsoon.GetProperty("status").GetString()));
        Assert.Equal(_world.Brand.Marketer.UserId, monsoon.GetProperty("owner").GetProperty("id").GetGuid());
        Assert.Equal(["Monsoon Waterproof Runner", "Rain Boots Classic"], monsoon.GetProperty("products").EnumerateArray().Select(product => product.GetProperty("title").GetString()).Order());
    }

    [Fact]
    public async Task The_list_filters_by_stage_status_owner_and_opening_date()
    {
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, _monsoon);
        (await Campaigns.PostAsync(_world.Marketer, _world.BrandId, _monsoon, "discard", new { version = Campaigns.Version(detail) })).EnsureSuccessStatusCode();
        var opened = _world.Api.Clock.GetUtcNow().AddDays(-2).ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd");
        var later = _world.Api.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd");

        Assert.Equal([_monsoon], await IdsAsync("?stage=discarded"));
        Assert.Equal([_school], await IdsAsync("?status=in_progress"));
        Assert.Equal([_school, _monsoon], await IdsAsync($"?owner={_world.Brand.Marketer.UserId}"));
        Assert.Empty(await IdsAsync($"?owner={_world.Approver.UserId}"));
        Assert.Equal([_monsoon], await IdsAsync($"?from={opened}&to={opened}"));
        Assert.Equal([_school], await IdsAsync($"?from={later}"));
    }

    [Fact]
    public async Task Search_matches_the_campaign_name_or_its_products()
    {
        Assert.Equal([_school], await IdsAsync("?q=school"));
        Assert.Equal([_monsoon], await IdsAsync("?q=rain%20boots"));
        Assert.Empty(await IdsAsync("?q=sandals"));
    }

    [Fact]
    public async Task The_list_pages_with_a_cursor()
    {
        var first = await Campaigns.ListAsync(_world.Marketer, _world.BrandId, "?limit=1");
        var next = first.GetProperty("nextBefore").GetString();
        var second = await Campaigns.ListAsync(_world.Marketer, _world.BrandId, $"?limit=1&before={next}");

        Assert.Equal([_school], first.GetProperty("campaigns").EnumerateArray().Select(card => card.GetProperty("id").GetGuid()));
        Assert.Equal([_monsoon], second.GetProperty("campaigns").EnumerateArray().Select(card => card.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Theory]
    [InlineData("?stage=everything", "stage")]
    [InlineData("?status=busy", "status")]
    [InlineData("?from=15-10-2026", "from")]
    [InlineData("?from=2026-10-20&to=2026-10-10", "to")]
    [InlineData("?before=yesterday", "before")]
    public async Task Bad_filters_are_rejected(string query, string field)
    {
        using var response = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Another_workspace_sees_none_of_these_campaigns()
    {
        var outsider = await Workspaces.SignInAsync(_world.Api);
        var other = await Workspaces.CreateAsync(outsider.Browser);

        Assert.Empty(await Campaigns.CardsAsync(outsider.Browser, other));
        var activity = await outsider.Browser.GetAsync($"{Campaigns.Base(other)}/{_monsoon}/activity", Token);
        Assert.Equal(HttpStatusCode.NotFound, activity.StatusCode);
    }

    [Fact]
    public async Task The_activity_log_belongs_to_one_room()
    {
        var monsoon = await Campaigns.ActivityAsync(_world.Approver.Browser, _world.BrandId, _monsoon);
        var school = await Campaigns.ActivityAsync(_world.Approver.Browser, _world.BrandId, _school);

        Assert.Equal(["stage_run.succeeded", "campaign_room.opened"], monsoon.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()));
        Assert.Equal(["stage_run.succeeded", "campaign_room.opened"], school.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()));
        Assert.NotEqual(
            monsoon.GetProperty("entries")[0].GetProperty("id").GetInt64(),
            school.GetProperty("entries")[0].GetProperty("id").GetInt64());
        Assert.Equal(_world.Brand.Marketer.Account.Name, monsoon.GetProperty("entries")[1].GetProperty("actorName").GetString());
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task<Guid[]> IdsAsync(string query) =>
        [.. (await Campaigns.CardsAsync(_world.Marketer, _world.BrandId, query)).Select(card => card.GetProperty("id").GetGuid())];
}
