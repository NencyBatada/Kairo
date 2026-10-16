using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.BrandKit;
using Kairo.Modules.CampaignRooms.Media;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class MediaTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Images_follow_the_copy_in_each_size_the_campaign_needs()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);

        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(0, await _world.RunStagesAsync());

        var request = Assert.Single(_world.Media.ImageRequests);
        var product = Assert.Single(request.GetProperty("products").EnumerateArray());
        Assert.Equal("p1", product.GetProperty("id").GetString());
        Assert.Equal(Convert.ToBase64String(FakeMedia.Photo), product.GetProperty("photos")[0].GetProperty("data").GetString());
        Assert.Equal(
            [("product", "landscape,square"), ("lifestyle", "landscape,square")],
            request.GetProperty("shots").EnumerateArray().Select(shot => (shot.GetProperty("style").GetString(), string.Join(",", shot.GetProperty("formats").EnumerateArray().Select(format => format.GetString())))));
        Assert.Empty(_world.Media.FootageRequests);
        var images = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId));
        Assert.Equal(4, images.Length);
        Assert.All(images, image => Assert.Equal("search", image.GetProperty("campaignType").GetString()));
        var square = images.First(image => image.GetProperty("format").GetString() == "square");
        Assert.Equal((1200, 1200, 1, "generated"), (square.GetProperty("width").GetInt32(), square.GetProperty("height").GetInt32(), square.GetProperty("version").GetInt32(), square.GetProperty("origin").GetString()));

        using var file = await _world.Marketer.GetAsync(square.GetProperty("mediaUrl").GetString(), Token);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/jpeg", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", file.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(FakeMedia.ImageBytes("square", 1), await file.Content.ReadAsByteArrayAsync(Token));
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("in_progress", Campaigns.Status(detail));
        Assert.False(detail.GetProperty("actions").GetProperty("continueWithoutVideo").GetBoolean());
    }

    [Fact]
    public async Task Demand_Gen_with_YouTube_gets_a_vertical_video_with_voiceover_from_the_images()
    {
        await ConnectYouTubeAsync();
        var logo = await BrandKits.UploadAsync(_world.Admin, _world.BrandId, BrandKits.Png, "logo.png", "logo");
        await BrandKits.ApproveNewAsync(_world.Admin, _world.BrandId, new
        {
            logo = new { fileId = logo },
            colours = new[] { new { hex = "#003366", name = "Navy", role = "primary" } },
        });
        var roomId = await DemandGenRoomAsync();

        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(0, await _world.RunStagesAsync());

        var shots = Assert.Single(_world.Media.ImageRequests).GetProperty("shots").EnumerateArray().ToList();
        Assert.Equal("landscape,square,portrait", string.Join(",", shots[0].GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
        Assert.Equal("landscape,square,portrait,vertical", string.Join(",", shots[1].GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
        var footage = Assert.Single(_world.Media.FootageRequests);
        Assert.Equal((2, 8), (footage.GetProperty("clips").GetInt32(), footage.GetProperty("clipSeconds").GetInt32()));
        Assert.Equal(Convert.ToBase64String(FakeMedia.ImageBytes("vertical", 1)), Assert.Single(footage.GetProperty("frames").EnumerateArray()).GetProperty("data").GetString());
        var video = Assert.Single(_world.Media.VideoRequests);
        Assert.Equal(("en", "Acme", 15, "#003366"), (video.GetProperty("language").GetString(), video.GetProperty("businessName").GetString(), video.GetProperty("seconds").GetInt32(), video.GetProperty("background").GetString()));
        Assert.Equal(("image/png", Convert.ToBase64String(BrandKits.Png)), (video.GetProperty("logo").GetProperty("mimeType").GetString(), video.GetProperty("logo").GetProperty("data").GetString()));
        Assert.Equal(
            [Convert.ToBase64String(FakeMedia.ClipBytes(0, 1)), Convert.ToBase64String(FakeMedia.ClipBytes(1, 1))],
            video.GetProperty("clips").EnumerateArray().Select(clip => clip.GetProperty("data").GetString()));

        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.DoesNotContain(Campaigns.AllAssets(set), asset => asset.GetProperty("kind").GetString() == "footage");
        var film = Campaigns.Asset(set, "video");
        Assert.Equal(("demand_gen", "vertical", 1080, 1920, 15m), (film.GetProperty("campaignType").GetString(), film.GetProperty("format").GetString(), film.GetProperty("width").GetInt32(), film.GetProperty("height").GetInt32(), film.GetProperty("durationSeconds").GetDecimal()));
        Assert.Equal("Shop Acme today", film.GetProperty("content").GetProperty("script")[1].GetProperty("caption").GetString());
        var voice = Campaigns.Asset(set, "voiceover");
        using var filmFile = await _world.Marketer.GetAsync(film.GetProperty("mediaUrl").GetString(), Token);
        using var voiceFile = await _world.Marketer.GetAsync(voice.GetProperty("mediaUrl").GetString(), Token);
        Assert.Equal("video/mp4", filmFile.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FakeMedia.VideoBytes(1), await filmFile.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal("audio/mpeg", voiceFile.Content.Headers.ContentType!.MediaType);
        Assert.Equal("in_progress", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task Demand_Gen_without_YouTube_runs_image_ads_only()
    {
        var roomId = await DemandGenRoomAsync();

        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(0, await _world.RunStagesAsync());

        Assert.Empty(_world.Media.FootageRequests);
        var shots = Assert.Single(_world.Media.ImageRequests).GetProperty("shots").EnumerateArray();
        Assert.All(shots, shot => Assert.DoesNotContain("vertical", shot.GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
        Assert.All(Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId)), image => Assert.Equal("demand_gen", image.GetProperty("campaignType").GetString()));
    }

    [Fact]
    public async Task A_failing_video_is_retried_once_then_the_marketer_can_continue_without_it()
    {
        await ConnectYouTubeAsync();
        _world.Media.FootageFails = true;
        var roomId = await DemandGenRoomAsync();
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        using var early = await SkipAsync(roomId);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await _world.RunStagesAsync());

        Assert.Equal(2, _world.Media.FootageRequests.Count);
        var failed = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("needs_attention", Campaigns.Status(failed));
        Assert.True(failed.GetProperty("actions").GetProperty("continueWithoutVideo").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, failed.GetProperty("actions").GetProperty("retryRun").ValueKind);
        var approverView = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.False(approverView.GetProperty("actions").GetProperty("continueWithoutVideo").GetBoolean());
        using var forbidden = await _world.Approver.Browser.PostAsJsonAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/skip-video", new { }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var skipped = await SkipAsync(roomId);

        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        var detail = await skipped.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("in_progress", Campaigns.Status(detail));
        Assert.False(detail.GetProperty("actions").GetProperty("continueWithoutVideo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("actions").GetProperty("retryRun").ValueKind);
        Assert.NotEmpty(Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId)));
        var actions = (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString());
        Assert.Contains("asset.video_skipped", actions);
        using var again = await SkipAsync(roomId);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Products_without_a_readable_photo_stop_the_images_without_retrying()
    {
        _world.Api.Websites.Status("https://acme.example/i/RAIN-1.jpg", HttpStatusCode.NotFound);
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Empty(_world.Media.ImageRequests);
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("needs_attention", Campaigns.Status(detail));
        var run = detail.GetProperty("runs")[0];
        Assert.Equal(("create_images", "failed", 1), (run.GetProperty("kind").GetString(), run.GetProperty("status").GetString(), run.GetProperty("attempts").GetInt32()));
        Assert.Equal("Kairo couldn't read a catalogue photo for any of the brief's products.", run.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Locked_images_are_kept_when_the_assets_are_sent_back()
    {
        var roomId = await CreatedAsync();
        var square = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId))
            .First(image => image.GetProperty("format").GetString() == "square");
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, square.GetProperty("id").GetGuid(), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var review = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "creating", comment = "Brighter images.", version = Campaigns.Version(review) });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        _world.Media.Generation = 2;

        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());

        var second = _world.Media.ImageRequests[1].GetProperty("shots").EnumerateArray().ToList();
        Assert.Equal("landscape", string.Join(",", second[0].GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
        var images = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId));
        Assert.Equal(4, images.Length);
        var kept = images.Single(image => image.GetProperty("id").GetGuid() == square.GetProperty("id").GetGuid());
        Assert.Equal((1, true), (kept.GetProperty("version").GetInt32(), kept.GetProperty("locked").GetBoolean()));
        Assert.All(images.Where(image => !image.GetProperty("locked").GetBoolean()), image => Assert.Equal(2, image.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task Regenerating_an_image_sends_the_instruction_for_that_one_size()
    {
        var roomId = await CreatedAsync();
        var landscape = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId))
            .First(image => image.GetProperty("format").GetString() == "landscape");
        _world.Media.Generation = 2;

        using var asked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, landscape.GetProperty("id").GetGuid(), "regenerate", new { version = 1, instruction = "Show it on a wet street" });
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        var request = _world.Media.ImageRequests[1];
        Assert.Equal("Show it on a wet street", request.GetProperty("instruction").GetString());
        var shot = Assert.Single(request.GetProperty("shots").EnumerateArray());
        Assert.Equal(("landscape", landscape.GetProperty("format").GetString()), (Assert.Single(shot.GetProperty("formats").EnumerateArray()).GetString(), "landscape"));
        var after = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId))
            .Single(image => image.GetProperty("id").GetGuid() == landscape.GetProperty("id").GetGuid());
        Assert.Equal((2, "regenerated"), (after.GetProperty("version").GetInt32(), after.GetProperty("origin").GetString()));
        Assert.EndsWith("version=2", after.GetProperty("mediaUrl").GetString());
        using var newer = await _world.Marketer.GetAsync(after.GetProperty("mediaUrl").GetString(), Token);
        using var older = await _world.Marketer.GetAsync(landscape.GetProperty("mediaUrl").GetString(), Token);
        Assert.Equal(FakeMedia.ImageBytes("landscape", 2), await newer.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(FakeMedia.ImageBytes("landscape", 1), await older.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task Regenerating_the_video_reuses_the_footage_unless_the_instruction_is_about_the_visuals()
    {
        await ConnectYouTubeAsync();
        var roomId = await DemandGenRoomAsync();
        for (var run = 0; run < 3; run++)
        {
            Assert.Equal(1, await _world.RunStagesAsync());
        }

        var film = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "video");
        _world.Media.Generation = 2;
        using var warmer = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, film.GetProperty("id").GetGuid(), "regenerate", new { version = 1, instruction = "Make the voiceover warmer" });
        Assert.Equal(HttpStatusCode.OK, warmer.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Single(_world.Media.FootageRequests);
        var reused = _world.Media.VideoRequests[1];
        Assert.Equal("Make the voiceover warmer", reused.GetProperty("instruction").GetString());
        Assert.Equal(Convert.ToBase64String(FakeMedia.ClipBytes(0, 1)), reused.GetProperty("clips")[0].GetProperty("data").GetString());
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal((2, "regenerated"), (Campaigns.Asset(set, "video").GetProperty("version").GetInt32(), Campaigns.Asset(set, "video").GetProperty("origin").GetString()));
        Assert.Equal((2, "regenerated"), (Campaigns.Asset(set, "voiceover").GetProperty("version").GetInt32(), Campaigns.Asset(set, "voiceover").GetProperty("origin").GetString()));

        _world.Media.Generation = 3;
        using var street = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, film.GetProperty("id").GetGuid(), "regenerate", new { version = 2, instruction = "Show it on a rainy street" });
        Assert.Equal(HttpStatusCode.OK, street.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(2, _world.Media.FootageRequests.Count);
        Assert.Equal("Show it on a rainy street", _world.Media.FootageRequests[1].GetProperty("instruction").GetString());
        Assert.Equal(Convert.ToBase64String(FakeMedia.ClipBytes(0, 3)), _world.Media.VideoRequests[2].GetProperty("clips")[0].GetProperty("data").GetString());
        var latest = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "video");
        using var file = await _world.Marketer.GetAsync(latest.GetProperty("mediaUrl").GetString(), Token);
        Assert.Equal(FakeMedia.VideoBytes(3), await file.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task Media_files_are_served_only_to_their_own_workspace()
    {
        var roomId = await CreatedAsync();
        var image = Images(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId))[0];
        var url = image.GetProperty("mediaUrl").GetString()!;
        var other = await Workspaces.SignInAsync(_world.Api);
        var otherBrand = await Workspaces.CreateAsync(other.Browser);

        using var stranger = await other.Browser.GetAsync(url, Token);
        using var elsewhere = await other.Browser.GetAsync(url.Replace(_world.BrandId.ToString(), otherBrand.ToString()), Token);
        using var unknownVersion = await _world.Marketer.GetAsync(url.Replace("version=1", "version=9"), Token);
        using var unknownAsset = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/{Guid.NewGuid()}/file", Token);
        using var approver = await _world.Approver.Browser.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAsset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approver.StatusCode);
    }

    [Theory]
    [InlineData("Make the voiceover warmer", false)]
    [InlineData("Shorter captions please", false)]
    [InlineData("Show it on a rainy street", true)]
    [InlineData("Make the video more festive", true)]
    [InlineData("Use a different camera angle", true)]
    public void Only_instructions_about_the_visuals_need_new_footage(string instruction, bool expected) =>
        Assert.Equal(expected, VideoMaker.NeedsNewFootage(instruction));

    [Fact]
    public void The_background_is_the_brand_kits_primary_colour()
    {
        var kit = JsonSerializer.SerializeToElement(new { colours = new object[] { new { hex = "#FFFFFF", role = "background" }, new { hex = "#003366", role = "primary" }, new { hex = "blue", role = "accent" } } });
        var secondary = JsonSerializer.SerializeToElement(new { colours = new[] { new { hex = "#112233", role = "secondary" } } });

        Assert.Equal("#003366", MediaDesk.Background(kit));
        Assert.Equal("#112233", MediaDesk.Background(secondary));
        Assert.Null(MediaDesk.Background(null));
        Assert.Null(MediaDesk.Background(JsonSerializer.SerializeToElement(new { colours = "none" })));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static JsonElement[] Images(JsonElement set) =>
        [.. Campaigns.AllAssets(set).Where(asset => asset.GetProperty("kind").GetString() == "image")];

    private Task<HttpResponseMessage> SkipAsync(Guid roomId) =>
        _world.Marketer.PostAsJsonAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/skip-video", new { }, Token);

    private async Task<Guid> CreatedAsync()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        return roomId;
    }

    private async Task<Guid> DemandGenRoomAsync()
    {
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "demand_gen" });
            return draft;
        };
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        return roomId;
    }

    private Task ConnectYouTubeAsync() => Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
}
