using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Files;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class AssetHistoryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Generated_copy_records_the_agent_call_the_products_and_the_brief()
    {
        var roomId = await CreatedAsync();
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");

        var history = await HistoryAsync(roomId, ad.GetProperty("id").GetGuid());

        var version = Assert.Single(history.GetProperty("versions").EnumerateArray());
        Assert.Equal((1, "generated", "search_ad"), (version.GetProperty("version").GetInt32(), version.GetProperty("origin").GetString(), history.GetProperty("kind").GetString()));
        var call = Assert.Single(version.GetProperty("agentCalls").EnumerateArray());
        Assert.Equal("create/copy", call.GetProperty("route").GetString());
        Assert.Matches("^sha256:[0-9a-f]{64}$", call.GetProperty("digest").GetString());
        Assert.Equal(
            ad.GetProperty("productIds").EnumerateArray().Select(id => id.GetString()),
            version.GetProperty("sources").GetProperty("productIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Empty(version.GetProperty("sources").GetProperty("photoUrls").EnumerateArray());
        Assert.True(version.GetProperty("briefVersion").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("previousVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("createdBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("approvedBy").ValueKind);
        Assert.Empty(version.GetProperty("memoryUses").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, version.GetProperty("stageRunId").ValueKind);
        var disclosure = version.GetProperty("disclosure");
        Assert.True(disclosure.GetProperty("aiGenerated").GetBoolean());
        Assert.False(disclosure.GetProperty("syntheticMedia").GetBoolean());
        Assert.Empty(disclosure.GetProperty("platformLabels").EnumerateArray());
        Assert.True(ad.GetProperty("disclosure").GetProperty("aiGenerated").GetBoolean());
    }

    [Fact]
    public async Task An_edit_records_who_changed_which_fields_from_which_version()
    {
        var roomId = await CreatedAsync();
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        using var edited = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, ad.GetProperty("id").GetGuid(), new { version = 1, path1 = "shoes" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var history = await HistoryAsync(roomId, ad.GetProperty("id").GetGuid());

        var versions = history.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal([2, 1], versions.Select(version => version.GetProperty("version").GetInt32()));
        var edit = versions[0];
        Assert.Equal(("edited", 1, "shoes"), (edit.GetProperty("origin").GetString(), edit.GetProperty("previousVersion").GetInt32(), edit.GetProperty("content").GetProperty("path1").GetString()));
        Assert.Equal(["path1"], edit.GetProperty("changedFields").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(_world.Brand.Marketer.UserId, edit.GetProperty("createdBy").GetProperty("id").GetGuid());
        Assert.Equal(_world.Brand.Marketer.Account.Name, edit.GetProperty("createdBy").GetProperty("name").GetString());
        Assert.Empty(edit.GetProperty("agentCalls").EnumerateArray());
        Assert.True(edit.GetProperty("disclosure").GetProperty("aiGenerated").GetBoolean());
        Assert.NotEqual(edit.GetProperty("content").GetProperty("path1").GetString(), versions[1].GetProperty("content").GetProperty("path1").GetString());
    }

    [Fact]
    public async Task Images_record_their_catalogue_photos_and_carry_the_google_ads_ai_label()
    {
        var roomId = await CreatedAsync();
        var image = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId))
            .First(asset => asset.GetProperty("kind").GetString() == "image");

        var version = Assert.Single((await HistoryAsync(roomId, image.GetProperty("id").GetGuid())).GetProperty("versions").EnumerateArray());

        Assert.Equal(["create/images"], version.GetProperty("agentCalls").EnumerateArray().Select(call => call.GetProperty("route").GetString()));
        Assert.Contains(
            version.GetProperty("sources").GetProperty("photoUrls").EnumerateArray().Select(url => url.GetString()),
            url => url!.StartsWith("https://acme.example/i/", StringComparison.Ordinal));
        Assert.NotEqual(JsonValueKind.Null, version.GetProperty("mediaUrl").ValueKind);
        var disclosure = image.GetProperty("disclosure");
        Assert.Equal(("synthid", true), (disclosure.GetProperty("watermark").GetString(), disclosure.GetProperty("syntheticMedia").GetBoolean()));
        var label = Assert.Single(disclosure.GetProperty("platformLabels").EnumerateArray());
        Assert.Equal(("google_ads", "ai_created"), (label.GetProperty("platform").GetString(), label.GetProperty("setting").GetString()));
        Assert.StartsWith("https://", label.GetProperty("source").GetString());
    }

    [Fact]
    public async Task The_video_records_its_footage_and_carries_the_youtube_and_google_ads_labels()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var roomId = await DemandGenRoomAsync();
        Assert.Equal(4, await RunAllAsync());
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var video = Campaigns.Asset(set, "video");

        var made = Assert.Single((await HistoryAsync(roomId, video.GetProperty("id").GetGuid())).GetProperty("versions").EnumerateArray());

        Assert.Equal(["create/footage", "create/video"], made.GetProperty("agentCalls").EnumerateArray().Select(call => call.GetProperty("route").GetString()));
        var inputs = made.GetProperty("sources").GetProperty("inputs").EnumerateArray().ToList();
        Assert.Equal(2, inputs.Count);
        Assert.All(inputs, input => Assert.Equal(("footage", 1), (input.GetProperty("kind").GetString(), input.GetProperty("version").GetInt32())));
        Assert.Equal(
            ["google_ads", "youtube"],
            video.GetProperty("disclosure").GetProperty("platformLabels").EnumerateArray().Select(label => label.GetProperty("platform").GetString()));
        Assert.Equal("contains_synthetic_media", video.GetProperty("disclosure").GetProperty("platformLabels")[1].GetProperty("setting").GetString());
        var voice = Campaigns.Asset(set, "voiceover");
        Assert.True(voice.GetProperty("disclosure").GetProperty("syntheticMedia").GetBoolean());
        Assert.Empty(voice.GetProperty("disclosure").GetProperty("platformLabels").EnumerateArray());
        using var footage = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/{inputs[0].GetProperty("assetId").GetGuid()}/history", Token);
        Assert.Equal(HttpStatusCode.NotFound, footage.StatusCode);

        var current = await CurrentAsync(roomId);
        var clip = current.First(asset => asset.Kind == "footage");
        Assert.True(clip.Hidden);
        Assert.Single(clip.Provenance.PromptDigests);
    }

    [Fact]
    public async Task A_video_made_again_from_the_same_footage_points_at_that_footage()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var roomId = await DemandGenRoomAsync();
        Assert.Equal(4, await RunAllAsync());
        var video = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "video");
        _world.Media.Generation = 2;
        using var regenerate = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, video.GetProperty("id").GetGuid(), "regenerate", new { version = 1, instruction = "Say it warmer" });
        Assert.Equal(HttpStatusCode.OK, regenerate.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        var latest = (await HistoryAsync(roomId, video.GetProperty("id").GetGuid())).GetProperty("versions")[0];

        Assert.Equal(("regenerated", "Say it warmer", 1), (latest.GetProperty("origin").GetString(), latest.GetProperty("instruction").GetString(), latest.GetProperty("previousVersion").GetInt32()));
        Assert.Equal(_world.Brand.Marketer.UserId, latest.GetProperty("createdBy").GetProperty("id").GetGuid());
        Assert.Equal(["create/video"], latest.GetProperty("agentCalls").EnumerateArray().Select(call => call.GetProperty("route").GetString()));
        Assert.Equal(2, latest.GetProperty("sources").GetProperty("inputs").GetArrayLength());
        Assert.All(latest.GetProperty("sources").GetProperty("inputs").EnumerateArray(), input => Assert.Equal(1, input.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task Approving_the_assets_records_the_approver_and_keeps_the_media_out_of_drafts()
    {
        var roomId = await CreatedAsync();
        var before = await CurrentAsync(roomId);
        var image = before.First(asset => asset.Kind == "image");
        Assert.StartsWith("drafts/", image.File!.Key);
        Assert.All(before.Where(asset => !asset.Hidden), asset => Assert.Equal(("passed", 100), (asset.CheckState, asset.Score)));

        using var approved = await ApproveAssetsAsync(roomId);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var after = await CurrentAsync(roomId);
        Assert.All(after.Where(asset => !asset.Hidden), asset => Assert.Equal(_world.Approver.UserId, asset.Provenance.ApprovedBy));
        var kept = after.Single(asset => asset.Id == image.Id);
        Assert.Equal(FileKeys.Kept(image.File.Key), kept.File!.Key);
        Assert.False(FileKeys.IsDraft(kept.File.Key));
        Assert.True(File.Exists(PathOf(kept.File.Key)));
        Assert.True(File.Exists(PathOf(image.File.Key)));
        Assert.Equal(1, kept.Version);
        var history = await HistoryAsync(roomId, image.Id);
        Assert.Equal(_world.Approver.Account.Name, history.GetProperty("versions")[0].GetProperty("approvedBy").GetProperty("name").GetString());
        var view = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId)).Single(asset => asset.GetProperty("id").GetGuid() == image.Id);
        using var file = await _world.Marketer.GetAsync(view.GetProperty("mediaUrl").GetString(), Token);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(await File.ReadAllBytesAsync(PathOf(image.File.Key), Token), await file.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task A_missing_media_file_stops_asset_approval_without_changing_anything()
    {
        var roomId = await CreatedAsync();
        var image = (await CurrentAsync(roomId)).First(asset => asset.Kind == "image");
        File.Delete(PathOf(image.File!.Key));

        using var refused = await ApproveAssetsAsync(roomId);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("A media file in this set is missing. Send the assets back so Kairo can make it again.", await Campaigns.TitleAsync(refused));
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(("asset_review", "waiting_approval"), (Campaigns.Stage(detail), Campaigns.Status(detail)));
        Assert.All(await CurrentAsync(roomId), asset => Assert.Null(asset.Provenance.ApprovedBy));
        Assert.All((await CurrentAsync(roomId)).Where(asset => asset.File is not null), asset => Assert.True(FileKeys.IsDraft(asset.File!.Key)));
    }

    [Fact]
    public async Task The_contract_reads_the_current_set_and_any_earlier_version()
    {
        var roomId = await CreatedAsync();
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        var id = ad.GetProperty("id").GetGuid();
        using var edited = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, path1 = "shoes" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var current = await CurrentAsync(roomId);
        var first = await VersionAsync(id, 1);
        var missing = await VersionAsync(id, 9);

        var latest = current.Single(asset => asset.Id == id);
        Assert.Equal((2, true, "edited", "shoes"), (latest.Version, latest.Current, latest.Provenance.Origin, latest.Content.GetProperty("path1").GetString()));
        Assert.Equal((1, false, "generated"), (first!.Version, first.Current, first.Provenance.Origin));
        Assert.Single(first.Provenance.PromptDigests);
        Assert.Null(first.File);
        Assert.True(first.Disclosure.AiGenerated);
        Assert.Null(missing);
        Assert.Null(await VersionAsync(Guid.NewGuid(), 1));
        Assert.Equal(["keyword_set", "search_ad", "shopping_title"], current.Where(asset => asset.File is null).Select(asset => asset.Kind).Order(StringComparer.Ordinal));
        Assert.All(current.Where(asset => asset.Kind == "image"), asset => Assert.Equal(FileBuckets.GeneratedMedia, asset.File!.Bucket));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task<Guid> CreatedAsync()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        Assert.Equal(1, await _world.RunStagesAsync());
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

    private async Task<int> RunAllAsync()
    {
        var total = 0;
        while (await _world.RunStagesAsync() is var ran and > 0)
        {
            total += ran;
        }

        return total;
    }

    private async Task<HttpResponseMessage> ApproveAssetsAsync(Guid roomId)
    {
        var ready = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "assets", version = Campaigns.Version(ready) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var review = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        return await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "assets", version = Campaigns.Version(review) });
    }

    private async Task<JsonElement> HistoryAsync(Guid roomId, Guid assetId)
    {
        using var response = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/assets/{assetId}/history", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<IReadOnlyList<CampaignAsset>> CurrentAsync(Guid roomId)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IAssets>().GetCurrentAsync(roomId, Token);
    }

    private async Task<CampaignAsset?> VersionAsync(Guid assetId, int version)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IAssets>().GetVersionAsync(assetId, version, Token);
    }

    private string PathOf(string key) =>
        Path.Combine([_world.Api.Services.GetRequiredService<FileStoreOptions>().Root, FileBuckets.GeneratedMedia, .. key.Split('/')]);
}
