using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class DraftingStartTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string AlreadyDrafting = "Kairo is already drafting the Brand Kit.";
    private const string DraftExists = "There's already a draft. Replace it to draft again.";
    private const string NotWebAddress = "Use a full web address starting with https://.";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_queues_a_pending_run_that_latest_returns()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await DraftingCalls.StartAsync(admin.Browser, brandId, "  https://acme.example/  ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("pending", run.GetProperty("status").GetString());
        Assert.Equal("https://acme.example/", run.GetProperty("websiteUrl").GetString());
        Assert.Equal(admin.UserId, run.GetProperty("requestedBy").GetGuid());
        Assert.Empty(run.GetProperty("pages").EnumerateArray());
        Assert.Empty(run.GetProperty("ads").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("finishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("version").ValueKind);
        Assert.Equal(run.GetProperty("id").GetGuid(), (await DraftingCalls.LatestAsync(admin.Browser, brandId)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Latest_is_empty_before_the_first_run()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync($"{DraftingCalls.Path(brandId)}/latest", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acme.example")]
    [InlineData("/about")]
    [InlineData("ftp://acme.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task Only_an_absolute_http_or_https_address_can_be_drafted(string? websiteUrl)
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await DraftingCalls.StartAsync(admin.Browser, brandId, websiteUrl);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { NotWebAddress }, await BrandKits.ErrorsAsync(response, "websiteUrl"));
    }

    [Fact]
    public async Task A_second_run_waits_until_the_first_one_finishes()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await DraftingCalls.StartedAsync(admin.Browser, brandId, "https://acme.example/");

        var second = await DraftingCalls.StartAsync(admin.Browser, brandId, "https://acme.example/");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(AlreadyDrafting, await BrandKits.ProblemTitleAsync(second));
    }

    [Fact]
    public async Task Two_admins_starting_at_once_get_one_run()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var other = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "admin");

        var responses = await Task.WhenAll(
            DraftingCalls.StartAsync(admin.Browser, brandId, "https://acme.example/"),
            DraftingCalls.StartAsync(other.Browser, brandId, "https://acme.example/"));

        Assert.Equal(
            new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order());
    }

    [Fact]
    public async Task An_existing_draft_is_replaced_only_when_asked()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        var refused = await DraftingCalls.StartAsync(admin.Browser, brandId, "https://acme.example/");
        var replacing = await DraftingCalls.StartAsync(admin.Browser, brandId, "https://acme.example/", replaceDraft: true);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(DraftExists, await BrandKits.ProblemTitleAsync(refused));
        Assert.Equal(HttpStatusCode.Accepted, replacing.StatusCode);
    }

    [Fact]
    public async Task Members_can_follow_drafting_but_only_admins_start_it()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");
        await DraftingCalls.StartedAsync(admin.Browser, brandId, "https://acme.example/");

        var start = await DraftingCalls.StartAsync(marketer.Browser, brandId, "https://acme.example/");
        var latest = await DraftingCalls.LatestAsync(marketer.Browser, brandId);

        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal("pending", latest.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_run()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await DraftingCalls.StartedAsync(admin.Browser, brandId, "https://acme.example/");
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync($"{DraftingCalls.Path(brandId)}/latest", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Starting_is_audited_and_published_to_admins()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var runId = await DraftingCalls.StartedAsync(admin.Browser, brandId, "https://acme.example/");

        Assert.Contains("brand_kit.drafting_started", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
        var published = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "brand_kit.drafting");
        var payload = JsonDocument.Parse(published.Payload).RootElement;
        Assert.Equal(runId, payload.GetProperty("runId").GetGuid());
        Assert.Equal("pending", payload.GetProperty("status").GetString());
        Assert.Equal("Roles", published.AudienceKind);
        Assert.Equal((int)BrandRoles.Admin, published.AudienceRoles);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
