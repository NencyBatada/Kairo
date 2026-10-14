using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitHistoryTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_lists_versions_newest_first_without_discarded_drafts()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("thrown away"), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("fourth"), 0);

        var history = await admin.Browser.GetFromJsonAsync<JsonElement>($"{BrandKits.Kit(brandId)}/versions", Token);

        Assert.Equal(
            new[] { (4, "draft"), (2, "approved"), (1, "superseded") },
            history.EnumerateArray().Select(version => (version.GetProperty("number").GetInt32(), version.GetProperty("status").GetString()!)).ToArray());
        Assert.All(history.EnumerateArray(), version => Assert.False(version.TryGetProperty("content", out _)));
        Assert.Equal(admin.UserId, history[1].GetProperty("approvedBy").GetGuid());
    }

    [Fact]
    public async Task A_version_can_be_read_with_its_content()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        var first = await admin.Browser.GetFromJsonAsync<JsonElement>($"{BrandKits.Kit(brandId)}/versions/1", Token);

        Assert.Equal("superseded", first.GetProperty("status").GetString());
        Assert.Equal("first", BrandKits.Summary(first));
    }

    [Fact]
    public async Task An_unknown_or_discarded_version_is_not_found()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();

        var discarded = await admin.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/1", Token);
        var unknown = await admin.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/99", Token);
        var restore = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound },
            new[] { discarded.StatusCode, unknown.StatusCode, restore.StatusCode });
    }

    [Fact]
    public async Task Restoring_copies_an_old_version_into_a_new_draft()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        var response = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(3, draft.GetProperty("number").GetInt32());
        Assert.Equal("restored", draft.GetProperty("origin").GetString());
        Assert.Equal(1, draft.GetProperty("restoredFrom").GetInt32());
        Assert.Equal("first", BrandKits.Summary(draft));
        Assert.Equal("second", BrandKits.Summary((await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("approved")));
    }

    [Fact]
    public async Task Restoring_replaces_the_current_draft_only_at_its_revision()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("third"), 0);

        var stale = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);
        var restored = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 1 }, Token);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var draft = await restored.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((3, 2, "first"), (draft.GetProperty("number").GetInt32(), draft.GetProperty("revision").GetInt32(), BrandKits.Summary(draft)));
    }

    [Fact]
    public async Task Only_a_superseded_version_can_be_restored()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);

        var approved = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 1 }, Token);
        var draft = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/2/restore", new { revision = 1 }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, approved.StatusCode);
        Assert.Equal("Only an older, superseded version can be restored.", await BrandKits.ProblemTitleAsync(approved));
        Assert.Equal(HttpStatusCode.BadRequest, draft.StatusCode);
    }

    [Fact]
    public async Task Marketers_can_read_the_history_but_not_restore()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");

        var history = await marketer.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions", Token);
        var version = await marketer.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/1", Token);
        var restore = await marketer.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden },
            new[] { history.StatusCode, version.StatusCode, restore.StatusCode });
    }

    [Fact]
    public async Task Restoring_is_audited()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        (await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token)).EnsureSuccessStatusCode();

        var entry = await Workspaces.LatestAuditEntryAsync(admin.Browser, brandId);
        Assert.Equal("brand_kit.restored", entry.GetProperty("action").GetString());
        Assert.Equal("3", entry.GetProperty("targetId").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
