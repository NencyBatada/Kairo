using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class BrandGuardianTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string Claim = "Claims more than the product can do.";

    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Checked_assets_reach_review_with_gate_two_open()
    {
        var roomId = await ApprovedAsync();

        Assert.Equal(3, await RunAllAsync());

        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(("asset_review", "waiting_approval"), (Campaigns.Stage(detail), Campaigns.Status(detail)));
        Assert.Equal("assets", detail.GetProperty("actions").GetProperty("approve").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("actions").GetProperty("approveBlocker").ValueKind);
        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var summary = set.GetProperty("check");
        var assets = Campaigns.AllAssets(set);
        Assert.Equal((100, assets.Length, 0, 0, 0, 80), (summary.GetProperty("score").GetInt32(), summary.GetProperty("passed").GetInt32(), summary.GetProperty("flagged").GetInt32(), summary.GetProperty("unchecked").GetInt32(), summary.GetProperty("settingsVersion").GetInt32(), summary.GetProperty("passScore").GetInt32()));
        Assert.All(assets, asset => Assert.Equal(("passed", 100), (Check(asset).GetProperty("state").GetString(), Check(asset).GetProperty("score").GetInt32())));
        Assert.All(assets, asset => Assert.True(asset.GetProperty("canSendBack").GetBoolean()));
        Assert.True(set.GetProperty("canSendBack").GetBoolean());
        var copy = Assert.Single(_world.Check.CallsTo("/check/copy"));
        Assert.Equal("en", copy.Body.GetProperty("language").GetString());
        var rules = copy.Body.GetProperty("rules").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()).ToList();
        Assert.Contains("brand.banned_words", rules);
        Assert.DoesNotContain("abcd.attract", rules);
        Assert.Equal(assets.Count(asset => asset.GetProperty("kind").GetString() == "image"), _world.Check.CallsTo("/check/image").Count);
        Assert.All(_world.Check.CallsTo("/check/image"), call =>
        {
            Assert.Matches("^p[0-9]+$", Assert.Single(call.Body.GetProperty("references").EnumerateArray()).GetProperty("id").GetString());
            Assert.NotEmpty(call.Body.GetProperty("image").GetProperty("data").GetString()!);
        });
        Assert.Contains("assets.checked", await ActionsAsync(roomId));

        using var approved = await ApproveAsync(roomId);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task A_failing_asset_is_fixed_and_checked_again_with_a_before_and_after_view()
    {
        _world.Check.Findings = (path, assetId, body) => FlagOriginalHeadline(path, assetId, body);
        var roomId = await ApprovedAsync();

        Assert.Equal(5, await RunAllAsync());

        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var ad = Campaigns.Asset(set, "search_ad");
        Assert.Equal((2, "fixed"), (ad.GetProperty("version").GetInt32(), ad.GetProperty("origin").GetString()));
        Assert.Equal(("passed", 100, 1), (Check(ad).GetProperty("state").GetString(), Check(ad).GetProperty("score").GetInt32(), Check(ad).GetProperty("fixAttempts").GetInt32()));
        var fix = Assert.Single(Check(ad).GetProperty("fixes").EnumerateArray());
        Assert.Equal((1, 2), (fix.GetProperty("fromVersion").GetInt32(), fix.GetProperty("toVersion").GetInt32()));
        var fixedIssue = Assert.Single(fix.GetProperty("issuesFixed").EnumerateArray());
        Assert.Equal(("policy.ads", "Google Ads policy risks", "block", Claim), (fixedIssue.GetProperty("rule").GetString(), fixedIssue.GetProperty("ruleName").GetString(), fixedIssue.GetProperty("severity").GetString(), fixedIssue.GetProperty("message").GetString()));
        Assert.Contains("Waterproof Running Shoes", fix.GetProperty("before").GetProperty("headlines").EnumerateArray().Select(text => text.GetString()));
        Assert.Contains("Quick-Dry Running Shoes", fix.GetProperty("after").GetProperty("headlines").EnumerateArray().Select(text => text.GetString()));
        var instruction = Assert.Single(_world.Create.Instructions).GetProperty("instruction").GetString();
        Assert.Contains(Claim, instruction);
        Assert.Contains("asset.fixed", await ActionsAsync(roomId));
        Assert.Equal("asset_review", Campaigns.Stage(await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task An_asset_still_failing_after_the_fix_limit_blocks_gate_two_until_its_flag_is_overridden()
    {
        await SaveSettingsAsync(new { version = 0, rules = (object?)null, customRules = (object?)null, passScore = 80, fixAttempts = 2 });
        _world.Check.Findings = (path, assetId, body) => FlagAd(path, assetId, body, "policy.ads", Claim);
        var roomId = await ApprovedAsync();

        await RunAllAsync();

        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var ad = Campaigns.Asset(set, "search_ad");
        Assert.Equal(3, ad.GetProperty("version").GetInt32());
        Assert.Equal(("failed", 70, false, 2), (Check(ad).GetProperty("state").GetString(), Check(ad).GetProperty("score").GetInt32(), Check(ad).GetProperty("passed").GetBoolean(), Check(ad).GetProperty("fixAttempts").GetInt32()));
        Assert.Equal(2, Check(ad).GetProperty("fixes").GetArrayLength());
        Assert.Equal(2, _world.Create.Instructions.Count);
        Assert.True(ad.GetProperty("canOverride").GetBoolean());
        Assert.Equal(1, set.GetProperty("check").GetProperty("flagged").GetInt32());
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal("asset_review", Campaigns.Stage(detail));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("actions").GetProperty("approve").ValueKind);
        Assert.Equal("1 asset still has open issues. Override its flag with a reason, or send it back.", detail.GetProperty("actions").GetProperty("approveBlocker").GetString());
        using var blocked = await ApproveAsync(roomId);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("1 asset still has open issues. Override its flag with a reason, or send it back.", await Campaigns.TitleAsync(blocked));

        using var overridden = await OverrideAsync(_world.Approver.Browser, roomId, ad, "Our lawyers approved this claim.");

        Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);
        var view = await overridden.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("overridden", view.GetProperty("checkState").GetString());
        Assert.Equal("Our lawyers approved this claim.", Check(view).GetProperty("overridden").GetProperty("reason").GetString());
        Assert.Equal(_world.Approver.UserId, Check(view).GetProperty("overridden").GetProperty("by").GetGuid());
        Assert.Contains("asset.flag_overridden", await ActionsAsync(roomId));
        using var approved = await ApproveAsync(roomId);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task Severities_and_the_pass_score_come_from_the_check_settings()
    {
        using var saved = await SaveSettingsAsync(new
        {
            version = 0,
            rules = new Dictionary<string, string> { ["policy.ads"] = "off" },
            customRules = (object?)null,
            passScore = 95,
            fixAttempts = 0,
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        _world.Check.Findings = (path, assetId, body) =>
            path == "/check/copy" && Kind(body, assetId) == "search_ad"
                ? [FakeCheck.Finding("brand.voice", "Sounds pushy for a warm brand.", "headlines", 1, "Stay Dry On Your Commute"), FakeCheck.Finding("policy.ads", "Off rule."), FakeCheck.Finding("made.up", "Unknown rule.")]
                : [];
        var roomId = await ApprovedAsync();

        await RunAllAsync();

        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var ad = Campaigns.Asset(set, "search_ad");
        var issue = Assert.Single(Check(ad).GetProperty("issues").EnumerateArray());
        Assert.Equal(("brand.voice", "Brand voice", "warn", "headlines", 1, "Stay Dry On Your Commute", true), (issue.GetProperty("rule").GetString(), issue.GetProperty("ruleName").GetString(), issue.GetProperty("severity").GetString(), issue.GetProperty("field").GetString(), issue.GetProperty("index").GetInt32(), issue.GetProperty("evidence").GetString(), issue.GetProperty("overridable").GetBoolean()));
        Assert.Equal(("failed", 90, 1), (Check(ad).GetProperty("state").GetString(), Check(ad).GetProperty("score").GetInt32(), Check(ad).GetProperty("settingsVersion").GetInt32()));
        Assert.Equal((1, 95, 0), (set.GetProperty("check").GetProperty("settingsVersion").GetInt32(), set.GetProperty("check").GetProperty("passScore").GetInt32(), set.GetProperty("check").GetProperty("fixAttempts").GetInt32()));
        Assert.Empty(_world.Create.Instructions);
        var copy = Assert.Single(_world.Check.CallsTo("/check/copy"));
        Assert.DoesNotContain("policy.ads", copy.Body.GetProperty("rules").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Image_specs_are_checked_in_code_and_their_flags_cannot_be_overridden()
    {
        await SaveSettingsAsync(new { version = 0, rules = (object?)null, customRules = (object?)null, passScore = 80, fixAttempts = 0 });
        _world.Media.ImageSize = (100, 100);
        var roomId = await ApprovedAsync();

        await RunAllAsync();

        var images = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId))
            .Where(asset => asset.GetProperty("kind").GetString() == "image")
            .ToList();
        Assert.NotEmpty(images);
        Assert.All(images, image =>
        {
            var issue = Assert.Single(Check(image).GetProperty("issues").EnumerateArray());
            Assert.Equal(("format.media_spec", false, "100×100"), (issue.GetProperty("rule").GetString(), issue.GetProperty("overridable").GetBoolean(), issue.GetProperty("evidence").GetString()));
            Assert.False(image.GetProperty("canOverride").GetBoolean());
        });

        using var refused = await OverrideAsync(_world.Approver.Browser, roomId, images[0], "It looks fine to me.");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Some of this asset's issues can't be overridden. Send it back instead.", await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task A_locked_political_asset_is_flagged_without_a_fix_and_can_be_sent_back_edited_and_checked_again()
    {
        _world.Create.Answer = request =>
        {
            var copy = JsonSerializer.SerializeToNode(FakeCreate.Copy(request))!;
            copy["adGroups"]![0]!["headlines"]![0] = "Shoes For Election Day";
            return copy;
        };
        var roomId = await ApprovedAsync();
        Assert.Equal(1, await _world.RunStagesAsync());
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, Id(ad), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);

        await RunAllAsync();

        ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId), "search_ad");
        var issue = Assert.Single(Check(ad).GetProperty("issues").EnumerateArray());
        Assert.Equal(("policy.political", false), (issue.GetProperty("rule").GetString(), issue.GetProperty("overridable").GetBoolean()));
        Assert.Equal(("failed", 1), (Check(ad).GetProperty("state").GetString(), ad.GetProperty("version").GetInt32()));
        Assert.Empty(_world.Create.Instructions);
        using var refused = await OverrideAsync(_world.Approver.Browser, roomId, ad, "Not about politics.");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        using var back = await SendBackAsync(_world.Approver.Browser, roomId, [Id(ad)], "Take out the election headline.");

        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var returned = await back.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("creating", "in_progress"), (Campaigns.Stage(returned), Campaigns.Status(returned)));
        Assert.False(returned.GetProperty("actions").GetProperty("requestCheck").GetBoolean());
        Assert.True((await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("actions").GetProperty("requestCheck").GetBoolean());
        Assert.Equal(0, await _world.RunStagesAsync());
        using var unlocked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, Id(ad), "unlock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        using var edited = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, Id(ad), new { version = 1, headlines = new[] { "Shoes For Rainy Days", "Stay Dry On Your Commute", "Dries In Minutes" } });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var ready = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "assets/check", new { version = Campaigns.Version(ready) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal("agent_working", Campaigns.Status(await requested.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.Equal(1, await _world.RunStagesAsync());

        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal("asset_review", Campaigns.Stage(detail));
        Assert.All(Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId)), asset => Assert.Equal("passed", Check(asset).GetProperty("state").GetString()));
        Assert.Contains("assets.check_requested", await ActionsAsync(roomId));
    }

    [Fact]
    public async Task Sending_one_asset_back_regenerates_only_that_asset_and_checks_it_again()
    {
        var roomId = await ApprovedAsync();
        await RunAllAsync();
        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var ad = Campaigns.Asset(set, "search_ad");

        using var back = await SendBackAsync(_world.Approver.Browser, roomId, [Id(ad)], "Mention quick-dry.");

        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var returned = await back.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("creating", "agent_working"), (Campaigns.Stage(returned), Campaigns.Status(returned)));
        Assert.Equal("Mention quick-dry.", returned.GetProperty("lastSendBack").GetProperty("comment").GetString());
        Assert.Equal(2, await RunAllAsync());
        var after = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var redone = Campaigns.Asset(after, "search_ad");
        Assert.Equal((2, "regenerated", "passed"), (redone.GetProperty("version").GetInt32(), redone.GetProperty("origin").GetString(), Check(redone).GetProperty("state").GetString()));
        Assert.Equal(1, Campaigns.Asset(after, "keyword_set").GetProperty("version").GetInt32());
        Assert.Equal("Mention quick-dry.", Assert.Single(_world.Create.Instructions).GetProperty("instruction").GetString());
        Assert.Single(_world.Create.Requests);
        Assert.Equal("asset_review", Campaigns.Stage(await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId)));
        Assert.Contains("campaign_room.sent_back", await ActionsAsync(roomId));
    }

    [Fact]
    public async Task Overrides_send_backs_and_new_checks_are_guarded()
    {
        await SaveSettingsAsync(new { version = 0, rules = (object?)null, customRules = (object?)null, passScore = 80, fixAttempts = 0 });
        _world.Check.Findings = (path, assetId, body) => FlagAd(path, assetId, body, "policy.ads", Claim);
        var roomId = await ApprovedAsync();
        await RunAllAsync();
        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var ad = Campaigns.Asset(set, "search_ad");
        var keywords = Campaigns.Asset(set, "keyword_set");
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var byMarketer = await OverrideAsync(_world.Marketer, roomId, ad, "Looks fine.");
        using var noReason = await OverrideAsync(_world.Approver.Browser, roomId, ad, " ");
        using var stale = await Campaigns.AssetActionAsync(_world.Approver.Browser, _world.BrandId, roomId, Id(ad), "override", new { version = 9, reason = "Looks fine." });
        using var notFlagged = await OverrideAsync(_world.Approver.Browser, roomId, keywords, "Looks fine.");
        using var nothingBack = await SendBackAsync(_world.Approver.Browser, roomId, [], "Change it.");
        using var unknownBack = await SendBackAsync(_world.Approver.Browser, roomId, [Guid.NewGuid()], "Change it.");
        using var marketerBack = await SendBackAsync(_world.Marketer, roomId, [Id(ad)], "Change it.");
        using var checkInReview = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "assets/check", new { version = Campaigns.Version(detail) });

        Assert.Equal(HttpStatusCode.Forbidden, byMarketer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, notFlagged.StatusCode);
        Assert.Equal("This asset has no open flags to override.", await Campaigns.TitleAsync(notFlagged));
        Assert.Equal(HttpStatusCode.BadRequest, nothingBack.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unknownBack.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, marketerBack.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, checkInReview.StatusCode);
        Assert.False(detail.GetProperty("actions").GetProperty("requestCheck").GetBoolean());
        var marketerView = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        Assert.False(marketerView.GetProperty("canOverride").GetBoolean());
        Assert.False(marketerView.GetProperty("canSendBack").GetBoolean());
    }

    [Fact]
    public async Task A_fix_that_fails_counts_as_an_attempt_and_the_room_still_reaches_review()
    {
        await SaveSettingsAsync(new { version = 0, rules = (object?)null, customRules = (object?)null, passScore = 80, fixAttempts = 1 });
        _world.Check.Findings = (path, assetId, body) => FlagAd(path, assetId, body, "policy.ads", Claim);
        var roomId = await ApprovedAsync();
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Create.Fails = true;

        Assert.Equal(4, await RunAllAsync());

        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(("asset_review", "waiting_approval"), (Campaigns.Stage(detail), Campaigns.Status(detail)));
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId), "search_ad");
        Assert.Equal((1, "failed", 1), (ad.GetProperty("version").GetInt32(), Check(ad).GetProperty("state").GetString(), Check(ad).GetProperty("fixAttempts").GetInt32()));
        Assert.Single(_world.Create.Instructions);
    }

    [Fact]
    public async Task An_asset_that_changes_during_a_check_is_checked_again()
    {
        var bumped = false;
        _world.Check.Findings = (path, assetId, body) =>
        {
            if (path == "/check/copy" && !bumped && Kind(body, assetId) == "search_ad")
            {
                bumped = true;
                BumpAsync(Guid.Parse(assetId)).GetAwaiter().GetResult();
            }

            return [];
        };
        var roomId = await ApprovedAsync();

        Assert.Equal(4, await RunAllAsync());

        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId), "search_ad");
        Assert.Equal((2, "passed"), (ad.GetProperty("version").GetInt32(), Check(ad).GetProperty("state").GetString()));
        Assert.Equal(2, _world.Check.CallsTo("/check/copy").Count);
        Assert.Single(_world.Check.CallsTo("/check/copy")[1].Body.GetProperty("assets").EnumerateArray());
        Assert.Equal("asset_review", Campaigns.Stage(await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task Checks_stop_after_twenty_runs_and_leave_unchecked_assets_for_the_reviewer()
    {
        _world.Check.Findings = (path, assetId, body) =>
        {
            if (path == "/check/copy" && Kind(body, assetId) == "search_ad")
            {
                BumpAsync(Guid.Parse(assetId)).GetAwaiter().GetResult();
            }

            return [];
        };
        var roomId = await ApprovedAsync();

        await RunAllAsync();

        Assert.Equal(20, _world.Check.CallsTo("/check/copy").Count);
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal("asset_review", Campaigns.Stage(detail));
        Assert.Equal("1 asset hasn't been checked by Brand Guardian yet. Send it back so it can be checked.", detail.GetProperty("actions").GetProperty("approveBlocker").GetString());
    }

    [Fact]
    public async Task Videos_are_checked_from_their_file_and_script()
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "demand_gen" });
            return draft;
        };
        var roomId = await ApprovedAsync();

        Assert.Equal(4, await RunAllAsync());

        var call = Assert.Single(_world.Check.CallsTo("/check/video"));
        var set = await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId);
        var video = Campaigns.Asset(set, "video");
        Assert.Equal(video.GetProperty("id").GetString(), call.Body.GetProperty("assetId").GetString());
        Assert.Equal("video/mp4", call.Body.GetProperty("video").GetProperty("mimeType").GetString());
        Assert.NotEmpty(call.Body.GetProperty("script").EnumerateArray());
        Assert.Contains("abcd.attract", call.Body.GetProperty("rules").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()));
        Assert.Equal("passed", Check(video).GetProperty("state").GetString());
        Assert.Equal("passed", Check(Campaigns.Asset(set, "voiceover")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_video_the_agent_cannot_read_is_flagged_as_a_spec_problem()
    {
        await SaveSettingsAsync(new { version = 0, rules = (object?)null, customRules = (object?)null, passScore = 80, fixAttempts = 0 });
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "demand_gen" });
            return draft;
        };
        _world.Check.BadVideo = true;
        var roomId = await ApprovedAsync();

        await RunAllAsync();

        var video = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Approver.Browser, _world.BrandId, roomId), "video");
        var issue = Assert.Single(Check(video).GetProperty("issues").EnumerateArray());
        Assert.Equal(("format.media_spec", "failed"), (issue.GetProperty("rule").GetString(), Check(video).GetProperty("state").GetString()));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task<Guid> ApprovedAsync()
    {
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

    private Task<HttpResponseMessage> SaveSettingsAsync(object body) =>
        _world.Admin.PutAsJsonAsync($"/api/brands/{_world.BrandId}/check-settings", body, Token);

    private async Task<HttpResponseMessage> ApproveAsync(Guid roomId)
    {
        var review = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        return await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "assets", version = Campaigns.Version(review) });
    }

    private Task<HttpResponseMessage> OverrideAsync(HttpClient browser, Guid roomId, JsonElement asset, string reason) =>
        Campaigns.AssetActionAsync(browser, _world.BrandId, roomId, Id(asset), "override", new { version = asset.GetProperty("version").GetInt32(), reason });

    private async Task<HttpResponseMessage> SendBackAsync(HttpClient browser, Guid roomId, Guid[] assetIds, string comment)
    {
        var detail = await Campaigns.DetailAsync(browser, _world.BrandId, roomId);
        return await Campaigns.PostAsync(browser, _world.BrandId, roomId, "assets/send-back", new { version = Campaigns.Version(detail), comment, assetIds });
    }

    private async Task<List<string?>> ActionsAsync(Guid roomId) =>
    [
        .. (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("action").GetString()),
    ];

    private async Task BumpAsync(Guid assetId)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        var db = scope.ServiceProvider.GetRequiredService<CampaignRoomsDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<AssetStore>();
        var asset = await db.Assets.SingleAsync(row => row.Id == assetId, Token);
        var current = (await store.CurrentAsync(asset, Token))!.Read();
        current.Path1 = $"v{asset.CurrentVersion + 1}";
        store.AddVersion(asset, current, AssetOrigins.Edited, null, null, null, _world.Brand.Marketer.UserId, null, ["path1"]);
        await db.SaveChangesAsync(Token);
    }

    private static IEnumerable<object> FlagOriginalHeadline(string path, string assetId, JsonElement body) =>
        path == "/check/copy" && Asset(body, assetId) is { } asset
            && asset.GetProperty("kind").GetString() == "search_ad"
            && asset.GetProperty("content").GetProperty("headlines").EnumerateArray().Any(text => text.GetString() == "Waterproof Running Shoes")
            ? [FakeCheck.Finding("policy.ads", Claim, "headlines", 0, "Waterproof Running Shoes")]
            : [];

    private static IEnumerable<object> FlagAd(string path, string assetId, JsonElement body, string rule, string message) =>
        path == "/check/copy" && Kind(body, assetId) == "search_ad" ? [FakeCheck.Finding(rule, message)] : [];

    private static string? Kind(JsonElement body, string assetId) => Asset(body, assetId)?.GetProperty("kind").GetString();

    private static JsonElement? Asset(JsonElement body, string assetId) =>
        body.GetProperty("assets").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(asset => asset!.Value.GetProperty("id").GetString() == assetId);

    private static JsonElement Check(JsonElement asset) => asset.GetProperty("check");

    private static Guid Id(JsonElement asset) => asset.GetProperty("id").GetGuid();
}
