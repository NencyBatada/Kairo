using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Media;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Ask;
using Kairo.Contracts.Ask;
using Kairo.IntegrationTests.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Kairo.SharedKernel.Agents;
using Kairo.Modules.Accessibility;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Kairo.IntegrationTests.Accessibility;

public sealed class MediaAlternativeTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static AlternativeContent Image(string text = "A red running shoe with reflective laces.") => new("en", text, null, [], null);

    [Fact]
    public async Task Alternatives_are_durable_reviewed_separately_and_reject_stale_edits()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        var (url, initial) = await GeneratedImageAsync(world);
        Assert.Equal("missing", initial.Status);
        Assert.False(initial.CanDraft);
        var request = new AlternativeWrite(initial.Target, initial.Revision, initial.SourceHash, Image());
        using var saved = await world.Marketer.PutAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility", request, Token);
        saved.EnsureSuccessStatusCode();
        var draft = (await saved.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        Assert.Equal("draft", draft.Status);
        Assert.Null(draft.ReviewedBy);
        using var stale = await world.Marketer.PutAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility", request, Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var confirmed = await world.Marketer.PostAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility/confirm", request with { Revision = draft.Revision }, Token);
        confirmed.EnsureSuccessStatusCode();
        var readback = (await world.Admin.GetFromJsonAsync<AlternativeView>(url, Token))!;
        Assert.Equal("reviewed", readback.Status);
        Assert.NotNull(readback.ReviewedBy);
        Assert.Empty(readback.Missing);
        using var changed = await world.Marketer.PutAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility", request with { Revision = readback.Revision, Content = Image("A corrected image description.") }, Token);
        changed.EnsureSuccessStatusCode();
        Assert.Equal("draft", (await changed.Content.ReadFromJsonAsync<AlternativeView>(Token))!.Status);
    }

    [Fact]
    public async Task Sql_policy_filters_alternatives_and_blocks_cross_brand_inserts()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        var (_, initial) = await GeneratedImageAsync(world);
        using var saved = await world.Marketer.PutAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility", new AlternativeWrite(initial.Target, 0, initial.SourceHash, Image()), Token);
        saved.EnsureSuccessStatusCode();
        using var scope = world.Api.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<Kairo.SharedKernel.Persistence.KairoDatabase>();
        var db = scope.ServiceProvider.GetRequiredService<Kairo.Modules.Accessibility.Data.AccessibilityDbContext>();
        await database.UseBrandAsync(world.BrandId, Token);
        Assert.Single(await db.Alternatives.IgnoreQueryFilters().ToListAsync(Token));
        await database.UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await db.Alternatives.IgnoreQueryFilters().ToListAsync(Token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [accessibility].[MediaAlternatives] (Id, BrandId, TargetKey, SourceHash, Content, Revision, UpdatedAt) VALUES ({Guid.NewGuid()}, {world.BrandId}, {"blocked"}, {"hash"}, {"{}"}, {1}, {DateTimeOffset.UtcNow})", Token));
    }

    [Fact]
    public async Task Source_changes_reset_review_and_wrong_brand_and_missing_versions_are_unavailable()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        var (url, initial) = await GeneratedImageAsync(world);
        var root = $"/api/brands/{world.BrandId}/media-accessibility";
        using var confirmed = await world.Marketer.PostAsJsonAsync(root + "/confirm", new AlternativeWrite(initial.Target, 0, initial.SourceHash, Image()), Token);
        confirmed.EnsureSuccessStatusCode();
        using var scope = world.Api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Kairo.SharedKernel.Persistence.KairoDatabase>().UseBrandAsync(world.BrandId, Token);
        var db = scope.ServiceProvider.GetRequiredService<Kairo.Modules.CampaignRooms.Data.CampaignRoomsDbContext>();
        var version = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.AssetVersions.IgnoreQueryFilters(), row => row.AssetId == initial.Target.Id, Token);
        var file = version.Read().Media!;
        await scope.ServiceProvider.GetRequiredService<IFileStore>().PutAsync(FileBuckets.GeneratedMedia, file.FileKey, [1, 2, 3], file.ContentType, Token);
        var changed = (await world.Marketer.GetFromJsonAsync<AlternativeView>(url, Token))!;
        Assert.Equal("missing", changed.Status);
        Assert.Null(changed.ReviewedBy);
        Assert.NotEqual(initial.SourceHash, changed.SourceHash);
        using var oldHash = await world.Marketer.PutAsJsonAsync(root, new AlternativeWrite(initial.Target, changed.Revision, initial.SourceHash, Image()), Token);
        Assert.Equal(HttpStatusCode.Conflict, oldHash.StatusCode);
        using var missingVersion = await world.Marketer.GetAsync(url.Replace("version=1", "version=99"), Token);
        Assert.Equal(HttpStatusCode.NotFound, missingVersion.StatusCode);
        await using var other = await CampaignWorld.CreateAsync(sqlServer);
        using var crossBrand = await other.Marketer.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.NotFound, crossBrand.StatusCode);
    }

    [Fact]
    public async Task Concurrent_saves_do_not_overwrite_and_drafting_never_bypasses_accounting()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        var (_, initial) = await GeneratedImageAsync(world);
        var root = $"/api/brands/{world.BrandId}/media-accessibility";
        var request = new AlternativeWrite(initial.Target, 0, initial.SourceHash, Image());
        var results = await Task.WhenAll(world.Marketer.PutAsJsonAsync(root, request, Token), world.Marketer.PutAsJsonAsync(root, request with { Content = Image("A blue running shoe.") }, Token));
        try { Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(response => response.StatusCode).OrderBy(status => status)); }
        finally { foreach (var response in results) response.Dispose(); }
        using var draft = await world.Marketer.PostAsJsonAsync(root + "/draft", request, Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, draft.StatusCode);
        Assert.Contains("accessibility_drafting_unavailable", await draft.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Ask_preview_alternatives_remain_private_to_the_conversation_owner()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        var (_, image) = await GeneratedImageAsync(world);
        var target = new AskTarget("asset", image.Target.Id, image.Target.CampaignId);
        var proposed = await AskHarness.ProposeAsync(world.Api, world.Marketer, world.BrandId, target, new { instruction = "Use a more festive product background" });
        var file = proposed.View.GetProperty("files")[0];
        var url = file.GetProperty("accessibilityUrl").GetString()!;
        using var own = await world.Marketer.GetAsync(url, Token);
        own.EnsureSuccessStatusCode();
        using var other = await world.Admin.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        var alternative = (await own.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        using var write = await world.Admin.PutAsJsonAsync($"/api/brands/{world.BrandId}/media-accessibility", new AlternativeWrite(alternative.Target, 0, alternative.SourceHash, Image()), Token);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        world.Api.Clock.Advance(TimeSpan.FromHours(1));
        using var expired = await world.Marketer.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
    }

    [Fact]
    public async Task Read_only_roles_cannot_confirm_or_draft_even_if_the_feature_setting_is_enabled()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer, configureServices: services => services.RemoveAll<IAgentInvocations>());
        var (url, initial) = await GeneratedImageAsync(world);
        var root = $"/api/brands/{world.BrandId}/media-accessibility";
        var request = new AlternativeWrite(initial.Target, 0, initial.SourceHash, Image());
        var read = (await world.Approver.Browser.GetFromJsonAsync<AlternativeView>(url, Token))!;
        Assert.False(read.CanEdit);
        using var denied = await world.Approver.Browser.PostAsJsonAsync(root + "/confirm", request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        world.Api.Services.GetRequiredService<IConfiguration>()["Kairo:Accessibility:AiDraftingEnabled"] = "true";
        using var draft = await world.Marketer.PostAsJsonAsync(root + "/draft", request, Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, draft.StatusCode);
        Assert.False((await world.Marketer.GetFromJsonAsync<AlternativeView>(url, Token))!.CanDraft);
        using var empty = await world.Marketer.PostAsJsonAsync(root + "/confirm", request with { Content = Image("") }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Described_file_and_captions_are_authorized_and_text_changes_clear_the_old_description()
    {
        await using var world = await CampaignWorld.CreateAsync(sqlServer);
        await Connections.ConnectAsync(world.Api, world.Admin, world.BrandId, "youtube");
        world.Plan.Answer = request =>
        {
            var result = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            result["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "demand_gen" });
            return result;
        };
        var room = await world.OpenWithBriefAsync();
        await world.ApproveBriefAsync(room);
        for (var i = 0; i < 10 && await world.RunStagesAsync() > 0; i++) { }
        var video = Campaigns.AllAssets(await Campaigns.AssetsAsync(world.Marketer, world.BrandId, room)).First(asset => asset.GetProperty("kind").GetString() == "video");
        var url = video.GetProperty("accessibilityUrl").GetString()!;
        var initial = (await world.Marketer.GetFromJsonAsync<AlternativeView>(url, Token))!;
        var root = $"/api/brands/{world.BrandId}/media-accessibility";
        var content = new AlternativeContent("en", null, "The product turns. [Music]", [new(0, 1, "[Music]")], "A red shoe turns.");
        var request = new AlternativeWrite(initial.Target, 0, initial.SourceHash, content);
        using var saved = await world.Marketer.PutAsJsonAsync(root, request, Token);
        saved.EnsureSuccessStatusCode();
        var draft = (await saved.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        var upload = root + "/described" + url[url.IndexOf('?')..] + $"&revision={draft.Revision}&sourceHash={draft.SourceHash}";
        using var mp4 = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112, 109, 112, 52, 50]);
        mp4.Headers.ContentType = new("video/mp4");
        using var uploaded = await world.Marketer.PostAsync(upload, mp4, Token);
        uploaded.EnsureSuccessStatusCode();
        var attached = (await uploaded.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        Assert.Empty(attached.Missing);
        Assert.Null(attached.ReviewedBy);
        using var captions = await world.Marketer.GetAsync(attached.CaptionsUrl, Token);
        Assert.StartsWith("WEBVTT", await captions.Content.ReadAsStringAsync(Token));
        Assert.Equal("text/vtt", captions.Content.Headers.ContentType!.MediaType);
        using var file = await world.Marketer.GetAsync(attached.DescribedMediaUrl, Token);
        file.EnsureSuccessStatusCode();
        Assert.True(file.Headers.CacheControl!.Private);
        Assert.True(file.Headers.CacheControl.NoStore);
        using var changedConfirmation = await world.Marketer.PostAsJsonAsync(root + "/confirm", request with { Revision = attached.Revision, Content = content with { VisualDescription = "A green shoe turns." } }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, changedConfirmation.StatusCode);
        using var confirmed = await world.Marketer.PostAsJsonAsync(root + "/confirm", request with { Revision = attached.Revision }, Token);
        confirmed.EnsureSuccessStatusCode();
        var reviewed = (await confirmed.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        using var oldTrack = await world.Marketer.GetAsync(attached.CaptionsUrl, Token);
        Assert.Equal(HttpStatusCode.NotFound, oldTrack.StatusCode);
        using var changed = await world.Marketer.PutAsJsonAsync(root, request with { Revision = reviewed.Revision, Content = content with { VisualDescription = "Corrected visual description." } }, Token);
        changed.EnsureSuccessStatusCode();
        var incomplete = (await changed.Content.ReadFromJsonAsync<AlternativeView>(Token))!;
        Assert.Null(incomplete.DescribedMediaUrl);
        Assert.Null(incomplete.ReviewedBy);
        Assert.NotEmpty(incomplete.Missing);
    }

    [Fact]
    public void Caption_validation_and_vtt_preserve_timing_and_escape_markup()
    {
        var content = new AlternativeContent("hi", null, "नमस्ते", [new(0, 1.25m, "<script> & नमस्ते")], "A product on a table.");
        Assert.Empty(AlternativeRules.Validate(content, 2));
        Assert.Contains("00:00:00.000 --> 00:00:01.250", AlternativeRules.Vtt(content.Captions));
        Assert.Contains("&lt;script&gt; &amp; नमस्ते", AlternativeRules.Vtt(content.Captions));
        Assert.NotEmpty(AlternativeRules.Validate(content with { Captions = [new(1, 3, "Over duration")] }, 2));
        Assert.NotEmpty(AlternativeRules.Validate(content with { Captions = [new(0, 1, "First"), new(.5m, 2, "Overlapping")] }, 2));
        Assert.Contains(AlternativeRules.Missing(content, "video/mp4", false), value => value.Contains("Audio-described"));
        Assert.Empty(AlternativeRules.Missing(content with { AudioConveysVisuals = true }, "video/mp4", false));
    }

    private static async Task<(string Url, AlternativeView View)> GeneratedImageAsync(CampaignWorld world)
    {
        var room = await world.OpenWithBriefAsync();
        await world.ApproveBriefAsync(room);
        for (var i = 0; i < 3; i++) await world.RunStagesAsync();
        var set = await Campaigns.AssetsAsync(world.Marketer, world.BrandId, room);
        var image = set.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("assets").EnumerateArray()).First(asset => asset.GetProperty("kind").GetString() == "image");
        var url = image.GetProperty("accessibilityUrl").GetString()!;
        return (url, (await world.Marketer.GetFromJsonAsync<AlternativeView>(url, Token))!);
    }
}
