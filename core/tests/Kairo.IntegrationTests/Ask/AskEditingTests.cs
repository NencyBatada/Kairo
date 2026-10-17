using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.BrandMemory;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.BrandKit;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Catalogue;
using Kairo.Modules.Ask.Data;
using Kairo.Modules.Ask;
using Kairo.Modules.Catalogue.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Ask;

public sealed class AskEditingTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private static CancellationToken Token => AskHarness.Token;
    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);
    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Brief_preview_does_not_edit_until_confirmation_and_confirmation_is_idempotent()
    {
        var room = await _world.OpenWithBriefAsync();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), new { title = "Rain ready runners" });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        Assert.NotEqual("Rain ready runners", (await BriefAsync(room)).GetProperty("brief").GetProperty("title").GetString());
        using var confirmed = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal("applied", (await AskHarness.JsonAsync(confirmed)).GetProperty("status").GetString());
        var after = await BriefAsync(room);
        Assert.Equal("Rain ready runners", after.GetProperty("brief").GetProperty("title").GetString());
        using var repeated = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal("applied", (await AskHarness.JsonAsync(repeated)).GetProperty("status").GetString());
        Assert.Equal(after.GetProperty("brief").GetProperty("version").GetInt32(), (await BriefAsync(room)).GetProperty("brief").GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("budget")]
    [InlineData("approved")]
    [InlineData("launch")]
    public async Task Dangerous_brief_fields_never_produce_an_applicable_preview(string field)
    {
        var room = await _world.OpenWithBriefAsync();
        var changes = new Dictionary<string, object> { [field] = field == "budget" ? 999999m : true };
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), changes);
        Assert.Equal("failed", proposed.View.GetProperty("status").GetString());
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("brief_draft", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
    }

    [Fact]
    public async Task Stale_expired_and_role_revoked_proposals_cannot_apply()
    {
        var room = await _world.OpenWithBriefAsync();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), new { title = "Proposed title" });
        var before = await BriefAsync(room);
        using var edit = await _world.Marketer.PatchAsJsonAsync($"/api/brands/{_world.BrandId}/campaigns/{room}/brief", new
        {
            version = before.GetProperty("campaignVersion").GetInt32(), briefVersion = before.GetProperty("brief").GetProperty("version").GetInt32(), title = "Someone else's title",
        }, Token);
        await AskHarness.JsonAsync(edit);
        using var stale = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var fresh = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), new { title = "Another preview" });
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(31));
        using var expired = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, fresh.Conversation, fresh.Proposal);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        var revoked = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), new { title = "Revoked preview" });
        using var roles = await _world.Admin.PutAsJsonAsync($"/api/brands/{_world.BrandId}/members/{_world.Brand.Marketer.UserId}/roles", new { roles = new[] { "approver" } }, Token);
        roles.EnsureSuccessStatusCode();
        using var forbidden = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, revoked.Conversation, revoked.Proposal);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("Someone else's title", (await BriefAsync(room)).GetProperty("brief").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Brand_kit_confirmation_saves_a_draft_and_preserves_the_approved_kit()
    {
        await BrandKits.ApproveNewAsync(_world.Admin, _world.BrandId, BrandKits.Content());
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Admin, _world.BrandId, new AskTarget("brand_kit"), new { voice = new { summary = "Clear and welcoming.", traits = new[] { "warm" } } });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(_world.Admin, _world.BrandId)).GetProperty("draft").ValueKind);
        using var confirm = await AskHarness.ConfirmAsync(_world.Admin, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var kit = await BrandKits.GetAsync(_world.Admin, _world.BrandId);
        Assert.Equal("Friendly and practical.", BrandKits.Summary(kit.GetProperty("approved")));
        Assert.Equal("Clear and welcoming.", BrandKits.Summary(kit.GetProperty("draft")));
    }

    [Fact]
    public async Task Catalogue_previews_only_fill_missing_imported_fields()
    {
        await Catalogues.ImportAsync(_world.Admin, _world.BrandId, Catalogues.Header + "MISSING,,https://acme.example/p/MISSING,https://acme.example/i/MISSING.jpg,,in_stock,Shoes\n", true);
        Guid id;
        await using (var scope = _world.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
            id = await scope.ServiceProvider.GetRequiredService<CatalogueDbContext>().BrandProducts.Where(row => row.OfferId == "MISSING").Select(row => row.Id).SingleAsync(Token);
        }
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("catalogue", id), new { title = "Monsoon runner", price = 1200m, currency = "INR" });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var product = await _world.Marketer.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/catalogue/products/{id}", Token);
        Assert.Equal("Monsoon runner", product.GetProperty("title").GetString());
        var reopened = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{AskHarness.Root(_world.BrandId)}/{proposed.Conversation}", Token);
        Assert.EndsWith($"/brand-kit/catalogue/products/{id}", reopened.GetProperty("messages")[0].GetProperty("evidence")[0].GetProperty("path").GetString());
        var forbidden = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("catalogue", id), new { stock = 20 });
        Assert.Equal("failed", forbidden.View.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Trigger_definition_changes_preserve_activation_and_require_admin()
    {
        using var created = await _world.Admin.PostAsJsonAsync($"/api/brands/{_world.BrandId}/triggers/rules", new
        {
            name = "Pune rain", source = "weather", condition = new { kind = "heavy_rain", threshold = 50, withinDays = 3 },
            regions = new[] { "pune" }, category = "Shoes", cooldownHours = 72,
        }, Token);
        var rule = (await AskHarness.JsonAsync(created)).GetProperty("rule");
        var id = rule.GetProperty("id").GetGuid();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Admin, _world.BrandId, new AskTarget("trigger_rule", id), new { name = "Pune monsoon", cooldownHours = 48 });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        using var confirm = await AskHarness.ConfirmAsync(_world.Admin, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var changed = (await _world.Admin.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/triggers/rules/{id}", Token)).GetProperty("rule");
        Assert.Equal("Pune monsoon", changed.GetProperty("name").GetString());
        Assert.Equal(rule.GetProperty("status").GetString(), changed.GetProperty("status").GetString());
        var forbidden = await AskHarness.ProposeAsync(_world.Api, _world.Admin, _world.BrandId, new AskTarget("trigger_rule", id), new { status = "paused" });
        Assert.Equal("failed", forbidden.View.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Memory_confirmation_updates_only_the_previewed_title_and_lesson()
    {
        Guid id;
        await using (var scope = _world.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
            id = await scope.ServiceProvider.GetRequiredService<IBrandMemory>().SaveAsync(new MemoryDraft("message", "worked", "Old title", "Old lesson", "{}", [], [], [], "test"), Token);
        }
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brand_memory", id), new { title = "Rain message", lesson = "Quick drying resonated with commuters." });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var memory = (await _world.Marketer.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/brand-memory/{id}", Token)).GetProperty("entry");
        Assert.Equal("Rain message", memory.GetProperty("title").GetString());
        Assert.Equal("worked", memory.GetProperty("outcome").GetString());
        var reopened = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{AskHarness.Root(_world.BrandId)}/{proposed.Conversation}", Token);
        Assert.EndsWith($"/brand-kit/memory/{id}", reopened.GetProperty("messages")[0].GetProperty("evidence")[0].GetProperty("path").GetString());
    }

    [Theory]
    [InlineData("image")]
    [InlineData("video")]
    public async Task Media_preview_is_private_preserves_original_and_confirmation_installs_exact_candidate(string kind)
    {
        var room = await MediaRoomAsync(kind);
        var asset = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).First(value => value.GetProperty("kind").GetString() == kind);
        var id = asset.GetProperty("id").GetGuid();
        _world.Media.Generation = 2;
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("asset", id, room), new { instruction = "Make it more festive with new visuals" });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        var current = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).Single(value => value.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, current.GetProperty("version").GetInt32());
        var url = proposed.View.GetProperty("files")[0].GetProperty("url").GetString();
        using var preview = await _world.Marketer.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var bytes = await preview.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal(kind == "video" ? FakeMedia.VideoBytes(2) : FakeMedia.ImageBytes(asset.GetProperty("format").GetString()!, 2), bytes);
        using var otherUser = await _world.Approver.Browser.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var applied = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).Single(value => value.GetProperty("id").GetGuid() == id);
        Assert.Equal(2, applied.GetProperty("version").GetInt32());
        using var actual = await _world.Marketer.GetAsync(applied.GetProperty("mediaUrl").GetString(), Token);
        Assert.Equal(bytes, await actual.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task Text_asset_preview_applies_exactly_the_shown_copy()
    {
        var room = await MediaRoomAsync("image");
        var asset = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room), "search_ad");
        var id = asset.GetProperty("id").GetGuid();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("asset", id, room), new { headlines = new[] { "Ready For Rain", "Waterproof Runners", "Dry Feet On Your Commute" } });
        Assert.Equal("ready", proposed.View.GetProperty("status").GetString());
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        await AskHarness.JsonAsync(confirm);
        var changed = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room), "search_ad");
        Assert.Equal(proposed.View.GetProperty("after").GetProperty("headlines").GetRawText(), changed.GetProperty("content").GetProperty("headlines").GetRawText());
        Assert.Equal("unchecked", changed.GetProperty("checkState").GetString());
    }

    [Fact]
    public async Task Rejected_and_failed_media_previews_never_replace_the_original()
    {
        var room = await MediaRoomAsync("image");
        var asset = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).First(value => value.GetProperty("kind").GetString() == "image");
        var target = new AskTarget("asset", asset.GetProperty("id").GetGuid(), room);
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, target, new { instruction = "Make the image festive" });
        using var rejected = await _world.Marketer.PostAsync($"{AskHarness.Root(_world.BrandId)}/{proposed.Conversation}/proposals/{proposed.Proposal}/reject", null, Token);
        await AskHarness.JsonAsync(rejected);
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        _world.Media.ImagesFail = true;
        var failed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, target, new { instruction = "Try again with new visuals" });
        Assert.Equal("failed", failed.View.GetProperty("status").GetString());
        var current = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).Single(value => value.GetProperty("id").GetGuid() == target.Id);
        Assert.Equal(1, current.GetProperty("version").GetInt32());
    }

    private Task<JsonElement> BriefAsync(Guid room) => _world.Marketer.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/campaigns/{room}/brief", Token);

    [Fact]
    public async Task Expired_job_leases_recover_and_stale_workers_cannot_overwrite_the_preview()
    {
        var room = await _world.OpenWithBriefAsync();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("brief", room), new { title = "Recovered preview" }, preview: false);
        var oldLease = Guid.NewGuid();
        await using (var scope = _world.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
            var db = scope.ServiceProvider.GetRequiredService<AskDbContext>();
            var row = await db.Proposals.SingleAsync(value => value.Id == proposed.Proposal, Token);
            row.Status = "generating";
            row.Lease = oldLease;
            row.Attempts = 1;
            row.DueAt = _world.Api.Clock.GetUtcNow().AddMinutes(-1);
            await db.SaveChangesAsync(Token);
        }
        await AskHarness.PreviewAsync(_world.Api);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
            await scope.ServiceProvider.GetRequiredService<AskPreviewRunner>().RunAsync(proposed.Proposal, oldLease, Token);
            var row = await scope.ServiceProvider.GetRequiredService<AskDbContext>().Proposals.SingleAsync(value => value.Id == proposed.Proposal, Token);
            Assert.Equal("ready", row.Status);
            Assert.Equal(2, row.Attempts);
            Assert.Null(row.Lease);
            Assert.Contains("Recovered preview", row.Preview);
        }
    }

    [Fact]
    public async Task Rejection_during_media_generation_wins_and_never_publishes_the_candidate()
    {
        var room = await MediaRoomAsync("image");
        var asset = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).First(value => value.GetProperty("kind").GetString() == "image");
        var target = new AskTarget("asset", asset.GetProperty("id").GetGuid(), room);
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, target, new { instruction = "Try festive lighting" }, preview: false);
        var previous = _world.Api.Agents.Respond;
        _world.Api.Agents.Respond = (path, body) =>
        {
            if (path == "/create/images")
            {
                using var rejected = _world.Marketer.PostAsync($"{AskHarness.Root(_world.BrandId)}/{proposed.Conversation}/proposals/{proposed.Proposal}/reject", null, Token).GetAwaiter().GetResult();
                rejected.EnsureSuccessStatusCode();
            }
            return previous(path, body);
        };
        await AskHarness.PreviewAsync(_world.Api);
        var final = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{AskHarness.Root(_world.BrandId)}/{proposed.Conversation}/proposals/{proposed.Proposal}", Token);
        Assert.Equal("rejected", final.GetProperty("status").GetString());
        Assert.Empty(final.GetProperty("files").EnumerateArray());
        var current = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).Single(value => value.GetProperty("id").GetGuid() == target.Id);
        Assert.Equal(1, current.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Locking_an_asset_after_preview_prevents_replacement()
    {
        var room = await MediaRoomAsync("image");
        var asset = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).First(value => value.GetProperty("kind").GetString() == "image");
        var id = asset.GetProperty("id").GetGuid();
        var proposed = await AskHarness.ProposeAsync(_world.Api, _world.Marketer, _world.BrandId, new AskTarget("asset", id, room), new { instruction = "Make it festive" });
        using var locked = await Campaigns.AssetActionAsync(_world.Marketer, _world.BrandId, room, id, "lock", new { version = 1 });
        await AskHarness.JsonAsync(locked);
        using var confirm = await AskHarness.ConfirmAsync(_world.Marketer, _world.BrandId, proposed.Conversation, proposed.Proposal);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        var current = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room)).Single(value => value.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, current.GetProperty("version").GetInt32());
        Assert.True(current.GetProperty("locked").GetBoolean());
    }

    private async Task<Guid> MediaRoomAsync(string kind)
    {
        if (kind == "video")
        {
            await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
            _world.Plan.Answer = request =>
            {
                var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
                draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "demand_gen" });
                return draft;
            };
        }
        var room = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(room);
        for (var i = 0; i < 10 && await _world.RunStagesAsync() > 0; i++) { }
        return room;
    }
}
