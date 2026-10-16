using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Catalogue;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class AssetTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Approving_the_brief_writes_the_ads_in_the_brief_language()
    {
        var roomId = await _world.OpenWithBriefAsync();

        var approved = await _world.ApproveBriefAsync(roomId);

        Assert.Equal(("creating", "agent_working"), (Campaigns.Stage(approved), Campaigns.Status(approved)));
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(0, await _world.RunStagesAsync());
        var request = Assert.Single(_world.Create.Requests);
        Assert.Equal("en", request.GetProperty("language").GetString());
        Assert.Equal(["search", "shopping"], request.GetProperty("campaignTypes").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal("Monsoon runners", request.GetProperty("brief").GetProperty("title").GetString());
        var product = Assert.Single(request.GetProperty("products").EnumerateArray());
        Assert.Equal("p1", product.GetProperty("id").GetString());
        Assert.Empty(request.GetProperty("locked").EnumerateArray());

        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);

        Assert.True(set.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, set.GetProperty("editBlocker").ValueKind);
        Assert.Equal(["en"], set.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        Assert.Equal(["search", "shopping"], set.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("campaignType").GetString()));
        Assert.Equal(["keyword_set", "search_ad", "shopping_title"], Campaigns.TextAssets(set).Select(asset => asset.GetProperty("kind").GetString()));
        var ad = Campaigns.Asset(set, "search_ad");
        Assert.Equal(("Rain runners", 1, "generated", "unchecked", false), (ad.GetProperty("adGroup").GetString(), ad.GetProperty("version").GetInt32(), ad.GetProperty("origin").GetString(), ad.GetProperty("checkState").GetString(), ad.GetProperty("locked").GetBoolean()));
        Assert.StartsWith("https://acme.example/p/", ad.GetProperty("content").GetProperty("finalUrl").GetString());
        Assert.Equal(JsonValueKind.Null, ad.GetProperty("mediaUrl").ValueKind);
        var productId = Assert.Single(ad.GetProperty("productIds").EnumerateArray()).GetString();
        var title = Campaigns.Asset(set, "shopping_title");
        Assert.Equal([productId], title.GetProperty("productIds").EnumerateArray().Select(id => id.GetString()));
        var currentTitle = title.GetProperty("content").GetProperty("currentTitle").GetString();
        Assert.Equal($"Acme {currentTitle} - Waterproof", title.GetProperty("content").GetProperty("title").GetString());
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(("creating", "in_progress"), (Campaigns.Stage(detail), Campaigns.Status(detail)));
        Assert.Contains("asset.generated", await ActionsAsync(roomId));
    }

    [Fact]
    public async Task Copy_outside_the_google_ads_limits_is_left_out()
    {
        _world.Create.Answer = request => new
        {
            adGroups = new[]
            {
                new
                {
                    name = "Rain runners",
                    productIds = new[] { "p1" },
                    keywords = new[] { new { text = "waterproof shoes", match = "fuzzy" } },
                    headlines = new[] { "Waterproof Running Shoes", "A headline over thirty characters", "Dries Fast" },
                    descriptions = new[] { "Keeps feet dry.", "Dries in minutes." },
                    path1 = "rain-shoes",
                    path2 = "runners",
                    finalUrl = (string?)null,
                },
            },
            demandGen = (object?)null,
            shoppingTitles = new[] { new { productId = "p7", title = "Unknown" } },
        };
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);

        await _world.RunStagesAsync();

        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var keywords = Assert.Single(Campaigns.AllAssets(set));
        Assert.Equal("keyword_set", keywords.GetProperty("kind").GetString());
        Assert.Equal("phrase", keywords.GetProperty("content").GetProperty("keywords")[0].GetProperty("match").GetString());
    }

    [Fact]
    public async Task A_marketer_edits_an_asset_within_the_limits()
    {
        var roomId = await CreatedAsync();
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        var id = ad.GetProperty("id").GetGuid();
        string[] headlines = ["Waterproof Running Shoes", "Stay Dry On Your Commute", "Built For The Monsoon"];

        using var edited = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, headlines, path2 = "" });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var view = await edited.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((2, "edited", true, "unchecked"), (view.GetProperty("version").GetInt32(), view.GetProperty("origin").GetString(), view.GetProperty("editedByPerson").GetBoolean(), view.GetProperty("checkState").GetString()));
        Assert.Equal(headlines, view.GetProperty("content").GetProperty("headlines").EnumerateArray().Select(text => text.GetString()));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("content").GetProperty("path2").ValueKind);
        Assert.Equal("Mesh runners that keep your feet dry.", view.GetProperty("content").GetProperty("descriptions")[0].GetString());

        using var tooLong = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 2, headlines = new[] { "Waterproof Running Shoes", "Dries Fast", "A headline over thirty characters" } });
        using var wrongField = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 2, title = "A title" });
        using var stale = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, headlines });
        using var same = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 2, headlines });
        using var approver = await Campaigns.EditAssetAsync(_world.Approver.Browser, _world.BrandId, roomId, id, new { version = 2, headlines });
        using var noVersion = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { headlines });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("30 characters", (await ErrorsAsync(tooLong)).GetProperty("headlines")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, wrongField.StatusCode);
        Assert.True((await ErrorsAsync(wrongField)).TryGetProperty("title", out _));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("This asset changed since you opened it. Refresh and try again.", await Campaigns.TitleAsync(stale));
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal("Nothing in the asset changed.", await Campaigns.TitleAsync(same));
        Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.Contains("asset.edited", await ActionsAsync(roomId));
    }

    [Fact]
    public async Task Each_kind_of_text_asset_checks_its_own_limits()
    {
        _world.Plan.Answer = request => Brief(request, ["search", "shopping", "demand_gen"]);
        var roomId = await CreatedAsync();
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var keywords = Campaigns.Asset(set, "keyword_set").GetProperty("id").GetGuid();
        var demandGen = Campaigns.Asset(set, "demand_gen_text").GetProperty("id").GetGuid();
        var title = Campaigns.Asset(set, "shopping_title").GetProperty("id").GetGuid();

        using var keywordEdit = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, keywords, new { version = 1, keywords = new[] { new { text = "Rain Boots", match = "Exact" } } });
        using var badMatch = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, keywords, new { version = 2, keywords = new[] { new { text = "rain boots", match = "fuzzy" } } });
        using var noShortHeadline = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, demandGen, new { version = 1, headlines = new[] { "Monsoon-ready shoes that dry very fast" } });
        using var badAction = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, demandGen, new { version = 1, callToAction = "click_here" });
        using var demandGenEdit = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, demandGen, new { version = 1, callToAction = "Learn_More", businessName = "Acme Shoes" });
        using var longTitle = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, title, new { version = 1, title = new string('a', 151) });
        using var titleEdit = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, title, new { version = 1, title = "Acme Runner Waterproof Mesh Shoe" });

        Assert.Equal(HttpStatusCode.OK, keywordEdit.StatusCode);
        Assert.Equal(("rain boots", "exact"), Keyword(await keywordEdit.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.Equal(HttpStatusCode.BadRequest, badMatch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noShortHeadline.StatusCode);
        Assert.Contains("up to 30 characters", (await ErrorsAsync(noShortHeadline)).GetProperty("headlines")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, badAction.StatusCode);
        Assert.Equal(HttpStatusCode.OK, demandGenEdit.StatusCode);
        Assert.Equal("learn_more", (await demandGenEdit.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("content").GetProperty("callToAction").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, titleEdit.StatusCode);
    }

    [Fact]
    public async Task A_locked_asset_cannot_be_edited_or_regenerated_until_it_is_unlocked()
    {
        var roomId = await CreatedAsync();
        var id = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad").GetProperty("id").GetGuid();

        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "lock", new { version = 1 });
        using var lockedAgain = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "lock", new { version = 1 });
        using var edit = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, path1 = "shoes" });
        using var regenerate = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "regenerate", new { version = 1, instruction = "Shorter" });

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.True((await locked.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("locked").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, lockedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("Unlock this asset before editing it.", await Campaigns.TitleAsync(edit));
        Assert.Equal(HttpStatusCode.Conflict, regenerate.StatusCode);

        using var unlocked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "unlock", new { version = 1 });
        using var editAfter = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, path1 = "shoes" });

        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, editAfter.StatusCode);
        var actions = await ActionsAsync(roomId);
        Assert.Contains("asset.locked", actions);
        Assert.Contains("asset.unlocked", actions);
    }

    [Fact]
    public async Task Regenerating_an_asset_follows_the_instruction()
    {
        var roomId = await CreatedAsync();
        var id = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad").GetProperty("id").GetGuid();

        using var tooShort = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "regenerate", new { version = 1, instruction = "x" });
        using var asked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "regenerate", new { version = 1, instruction = "  Mention quick-dry  " });

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.NotEqual(Guid.Empty, (await asked.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("runId").GetGuid());
        var working = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.False(working.GetProperty("canEdit").GetBoolean());
        Assert.Equal("Wait for the Create agent to finish.", working.GetProperty("editBlocker").GetString());
        Assert.Equal("agent_working", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
        using var busy = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, id, new { version = 1, path1 = "shoes" });
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);

        Assert.Equal(1, await _world.RunStagesAsync());

        var request = Assert.Single(_world.Create.Instructions);
        Assert.Equal(("Mention quick-dry", "search_ad", "en"), (request.GetProperty("instruction").GetString(), request.GetProperty("kind").GetString(), request.GetProperty("language").GetString()));
        Assert.Equal("Rain runners", request.GetProperty("current").GetProperty("adGroup").GetString());
        Assert.Equal(["p1"], request.GetProperty("current").GetProperty("productIds").EnumerateArray().Select(key => key.GetString()));
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        Assert.Equal((2, "regenerated", false), (ad.GetProperty("version").GetInt32(), ad.GetProperty("origin").GetString(), ad.GetProperty("editedByPerson").GetBoolean()));
        Assert.Equal("Quick-Dry Running Shoes", ad.GetProperty("content").GetProperty("headlines")[0].GetString());
        Assert.Equal("quick-dry", ad.GetProperty("content").GetProperty("path1").GetString());
        Assert.StartsWith("https://acme.example/p/", ad.GetProperty("content").GetProperty("finalUrl").GetString());
        Assert.Contains("asset.regenerated", await ActionsAsync(roomId));
    }

    [Fact]
    public async Task A_rewrite_outside_the_limits_fails_the_run_and_keeps_the_asset()
    {
        _world.Create.Revision = _ => new { headlines = new[] { "Only one headline" }, descriptions = new[] { "One." } };
        var roomId = await CreatedAsync();
        var id = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad").GetProperty("id").GetGuid();
        using var asked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, id, "regenerate", new { version = 1, instruction = "Shorter" });
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            _world.Api.Clock.Advance(TimeSpan.FromMinutes(3));
            await _world.RunStagesAsync();
        }

        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId), "search_ad");
        Assert.Equal(1, ad.GetProperty("version").GetInt32());
        Assert.Equal("needs_attention", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task Sending_assets_back_rewrites_only_the_unlocked_ones()
    {
        var roomId = await CreatedAsync();
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        var title = Campaigns.Asset(set, "shopping_title");
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, title.GetProperty("id").GetGuid(), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));

        var review = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        using var blocked = await Campaigns.EditAssetAsync(_world.Marketer, _world.BrandId, roomId, Campaigns.Asset(review, "search_ad").GetProperty("id").GetGuid(), new { version = 1, path1 = "shoes" });

        Assert.False(review.GetProperty("canEdit").GetBoolean());
        Assert.Equal("Assets can be changed only while Kairo is creating them.", review.GetProperty("editBlocker").GetString());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "creating", comment = "Make the ads warmer.", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("agent_working", Campaigns.Status(await back.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.Equal(1, await _world.RunStagesAsync());

        var second = _world.Create.Requests[1];
        var lockedCopy = Assert.Single(second.GetProperty("locked").EnumerateArray());
        Assert.Equal("shopping_title", lockedCopy.GetProperty("kind").GetString());
        Assert.Equal(title.GetProperty("content").GetProperty("title").GetString(), lockedCopy.GetProperty("texts")[0].GetString());
        var after = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(1, Campaigns.Asset(after, "shopping_title").GetProperty("version").GetInt32());
        Assert.Equal(2, Campaigns.Asset(after, "search_ad").GetProperty("version").GetInt32());
        Assert.Equal(2, Campaigns.Asset(after, "keyword_set").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task An_ad_group_the_agent_drops_is_removed_unless_it_is_locked()
    {
        var roomId = await CreatedAsync();
        var set = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, roomId, Campaigns.Asset(set, "keyword_set").GetProperty("id").GetGuid(), "lock", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        _world.Create.Answer = request => new { adGroups = Array.Empty<object>(), demandGen = (object?)null, shoppingTitles = new[] { new { productId = "p1", title = "Acme Runner Waterproof" } } };
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "creating", comment = "Try again.", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);

        await _world.RunStagesAsync();

        var after = await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(["keyword_set", "shopping_title"], Campaigns.TextAssets(after).Select(asset => asset.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Shopping_alone_with_nothing_in_stock_needs_attention()
    {
        _world.Plan.Answer = request => Brief(request, ["shopping"]);
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        await Catalogues.ImportAsync(_world.Admin, _world.BrandId, Radar.Catalogue("out_of_stock"));

        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Empty(_world.Create.Requests);
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("needs_attention", Campaigns.Status(detail));
        Assert.Empty(Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    private async Task<Guid> CreatedAsync()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        return roomId;
    }

    private async Task<List<string?>> ActionsAsync(Guid roomId) =>
    [
        .. (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("action").GetString()),
    ];

    private static object Brief(JsonElement request, string[] types)
    {
        var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
        draft["campaignTypes"] = JsonSerializer.SerializeToNode(types);
        return draft;
    }

    private static (string?, string?) Keyword(JsonElement view)
    {
        var keyword = view.GetProperty("content").GetProperty("keywords")[0];
        return (keyword.GetProperty("text").GetString(), keyword.GetProperty("match").GetString());
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();
}
