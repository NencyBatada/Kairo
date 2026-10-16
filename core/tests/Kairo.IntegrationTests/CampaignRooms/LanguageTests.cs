using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class LanguageTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Every_brief_language_gets_its_own_ads_and_shopping_titles_stay_in_the_feed_language()
    {
        var roomId = await ApprovedAsync(["en", "hi"], ["search", "shopping", "demand_gen"]);

        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(
            [("en", "search,shopping,demand_gen"), ("hi", "search,demand_gen")],
            _world.Create.Requests
                .Select(request => (request.GetProperty("language").GetString(), string.Join(",", request.GetProperty("campaignTypes").EnumerateArray().Select(type => type.GetString()))))
                .OrderBy(pair => pair.Item1));
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(["en", "hi"], set.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        Assert.Equal(
            [("search", "en", "English"), ("search", "hi", "Hindi"), ("shopping", "en", "English"), ("demand_gen", "en", "English"), ("demand_gen", "hi", "Hindi")],
            Groups(set));
        Assert.All(
            Campaigns.AllAssets(set).Where(asset => asset.GetProperty("kind").GetString() == "shopping_title"),
            title => Assert.Equal("en", title.GetProperty("language").GetString()));
        Assert.Equal(
            ["demand_gen_text", "keyword_set", "search_ad"],
            Campaigns.AllAssets(set).Where(asset => asset.GetProperty("language").GetString() == "hi").Select(asset => asset.GetProperty("kind").GetString()).Order());

        var hindi = await AssetsAsync(roomId, "?language=hi");

        Assert.Equal([("search", "hi", "Hindi"), ("demand_gen", "hi", "Hindi")], Groups(hindi));
        Assert.Equal(["en", "hi"], hindi.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        using var unknown = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets?language=xx", Token);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_hindi_brief_still_gets_shopping_titles_in_the_feed_language()
    {
        var roomId = await ApprovedAsync(["hi"], ["search", "shopping"]);

        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(
            [("en", "shopping"), ("hi", "search")],
            _world.Create.Requests
                .Select(request => (request.GetProperty("language").GetString(), string.Join(",", request.GetProperty("campaignTypes").EnumerateArray().Select(type => type.GetString()))))
                .OrderBy(pair => pair.Item1));
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal([("search", "hi", "Hindi"), ("shopping", "en", "English")], Groups(set));
    }

    [Fact]
    public async Task Dropping_a_language_removes_its_ads_unless_they_are_locked()
    {
        var roomId = await ApprovedAsync(["en", "hi"], ["search"]);
        Assert.Equal(1, await _world.RunStagesAsync());
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var hindiAd = Campaigns.AllAssets(set).Single(asset => asset.GetProperty("kind").GetString() == "search_ad" && asset.GetProperty("language").GetString() == "hi");
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, hindiAd.GetProperty("id").GetGuid(), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        await _world.RunStagesAsync();
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var review = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "brief_draft", comment = "English only.", version = Campaigns.Version(review) });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var returned = await back.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var edited = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version = Campaigns.Version(returned), briefVersion = 1, languages = new[] { "en" } });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await _world.RunStagesAsync();
        var requests = _world.Create.Requests.Count;

        await _world.ApproveBriefAsync(roomId);
        Assert.Equal(1, await _world.RunStagesAsync());

        var latest = Assert.Single(_world.Create.Requests.Skip(requests));
        Assert.Equal("en", latest.GetProperty("language").GetString());
        var after = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(
            [("en", "keyword_set"), ("en", "search_ad"), ("hi", "search_ad")],
            Campaigns.TextAssets(after).Select(asset => (asset.GetProperty("language").GetString(), asset.GetProperty("kind").GetString())).Order());
        Assert.Equal(["en", "hi"], after.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
    }

    [Fact]
    public async Task Each_language_with_a_voice_gets_a_video_from_one_set_of_footage()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var roomId = await ApprovedAsync(["hi", "en", "mr"], ["search", "demand_gen"]);

        for (var run = 0; run < 3; run++)
        {
            Assert.Equal(1, await _world.RunStagesAsync());
        }

        while (await _world.RunStagesAsync() > 0)
        {
        }

        Assert.All(Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId)), asset => Assert.Equal("passed", asset.GetProperty("checkState").GetString()));
        Assert.Single(_world.Media.FootageRequests);
        Assert.Equal(["en", "hi"], _world.Media.VideoRequests.Select(request => request.GetProperty("language").GetString()).Order());
        Assert.All(_world.Media.VideoRequests, request => Assert.Equal(Convert.ToBase64String(FakeMedia.ClipBytes(0, 1)), request.GetProperty("clips")[0].GetProperty("data").GetString()));
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(
            [("en", "video"), ("en", "voiceover"), ("hi", "video"), ("hi", "voiceover")],
            Campaigns.AllAssets(set)
                .Where(asset => asset.GetProperty("kind").GetString() is "video" or "voiceover")
                .Select(asset => (asset.GetProperty("language").GetString(), asset.GetProperty("kind").GetString()))
                .Order());
        Assert.All(
            Campaigns.AllAssets(set).Where(asset => asset.GetProperty("kind").GetString() == "image"),
            image => Assert.Equal("zxx", image.GetProperty("language").GetString()));
        Assert.Contains(Campaigns.AllAssets(set), asset => asset.GetProperty("language").GetString() == "mr" && asset.GetProperty("kind").GetString() == "search_ad");
        Assert.Equal(["en", "hi", "mr"], set.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        var hindi = await AssetsAsync(roomId, "?language=hi");
        Assert.Contains(Campaigns.AllAssets(hindi), asset => asset.GetProperty("kind").GetString() == "image");
        Assert.DoesNotContain(Campaigns.AllAssets(hindi), asset => asset.GetProperty("language").GetString() == "en");
    }

    [Fact]
    public async Task Each_language_records_its_own_calls_and_the_shared_footage()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var roomId = await ApprovedAsync(["en", "hi"], ["search", "demand_gen"]);
        for (var run = 0; run < 3; run++)
        {
            Assert.Equal(1, await _world.RunStagesAsync());
        }

        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var footage = new List<HashSet<Guid>>();
        foreach (var language in new[] { "en", "hi" })
        {
            foreach (var kind in new[] { "video", "voiceover" })
            {
                var asset = Campaigns.AllAssets(set).Single(entry => entry.GetProperty("kind").GetString() == kind && entry.GetProperty("language").GetString() == language);
                var version = Assert.Single((await HistoryAsync(roomId, asset.GetProperty("id").GetGuid())).GetProperty("versions").EnumerateArray());
                Assert.Equal(
                    [("create/footage", null), ("create/video", language)],
                    version.GetProperty("agentCalls").EnumerateArray().Select(call => (call.GetProperty("route").GetString(), call.GetProperty("language").GetString())));
                var inputs = version.GetProperty("sources").GetProperty("inputs").EnumerateArray().ToList();
                Assert.All(inputs, input => Assert.Equal(("footage", 1), (input.GetProperty("kind").GetString(), input.GetProperty("version").GetInt32())));
                footage.Add([.. inputs.Select(input => input.GetProperty("assetId").GetGuid())]);
            }
        }

        Assert.Equal(2, footage[0].Count);
        Assert.All(footage, ids => Assert.True(ids.SetEquals(footage[0])));
        var hindiAd = Campaigns.AllAssets(set).Single(entry => entry.GetProperty("kind").GetString() == "search_ad" && entry.GetProperty("language").GetString() == "hi");
        var written = Assert.Single((await HistoryAsync(roomId, hindiAd.GetProperty("id").GetGuid())).GetProperty("versions").EnumerateArray());
        Assert.Equal([("create/copy", "hi")], written.GetProperty("agentCalls").EnumerateArray().Select(call => (call.GetProperty("route").GetString(), call.GetProperty("language").GetString())));
    }

    [Fact]
    public async Task Regenerating_a_video_keeps_a_locked_voiceover_and_updates_the_length()
    {
        var roomId = await VideoRoomAsync();
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var voiceover = Campaigns.Asset(set, "voiceover");
        var film = Campaigns.Asset(set, "video");
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, voiceover.GetProperty("id").GetGuid(), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        _world.Media.Generation = 2;

        using var regenerated = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, film.GetProperty("id").GetGuid(), "regenerate", new { version = 1, instruction = "Make it warmer" });
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        var request = _world.Media.VideoRequests[^1];
        Assert.Equal(
            [("Rain again?", "Rain again?"), ("Shop Acme today.", "Shop Acme today")],
            request.GetProperty("script").EnumerateArray().Select(line => (line.GetProperty("voiceover").GetString(), line.GetProperty("caption").GetString())));
        var after = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var newFilm = Campaigns.Asset(after, "video");
        Assert.Equal((2, 14.5m), (newFilm.GetProperty("version").GetInt32(), newFilm.GetProperty("durationSeconds").GetDecimal()));
        Assert.Equal((1, true), (Campaigns.Asset(after, "voiceover").GetProperty("version").GetInt32(), Campaigns.Asset(after, "voiceover").GetProperty("locked").GetBoolean()));
    }

    [Fact]
    public async Task A_failing_video_regeneration_is_tried_twice()
    {
        var roomId = await VideoRoomAsync();
        var film = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "video");
        _world.Media.FootageFails = true;

        using var regenerated = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, film.GetProperty("id").GetGuid(), "regenerate", new { version = 1, instruction = "Show it on a rainy street" });
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await _world.RunStagesAsync());

        Assert.Equal(3, _world.Media.FootageRequests.Count);
        Assert.Equal("needs_attention", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static IEnumerable<(string?, string?, string?)> Groups(JsonElement set) =>
        set.GetProperty("groups").EnumerateArray()
            .Where(group => group.GetProperty("language").GetString() != "zxx")
            .Select(group => (group.GetProperty("campaignType").GetString(), group.GetProperty("language").GetString(), group.GetProperty("languageName").GetString()));

    private Task<JsonElement> HistoryAsync(Guid roomId, Guid assetId) =>
        _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/{assetId}/history", Token);

    private Task<JsonElement> AssetsAsync(Guid roomId, string query) =>
        _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets{query}", Token);

    private async Task<Guid> ApprovedAsync(string[] languages, string[] types)
    {
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(types);
            draft["languages"] = JsonSerializer.SerializeToNode(languages);
            return draft;
        };
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        return roomId;
    }

    private async Task<Guid> VideoRoomAsync()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var roomId = await ApprovedAsync(["en"], ["search", "demand_gen"]);
        for (var run = 0; run < 4; run++)
        {
            Assert.Equal(1, await _world.RunStagesAsync());
        }

        return roomId;
    }
}
