using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.BrandKit.Drafting;
using Kairo.SharedKernel.Files;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class DraftingRunTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string Unreadable = "https://nothing.example/";
    private const string FailedMessage = "Kairo couldn't draft the Brand Kit this time. Try again, or fill it in by hand.";

    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString);
        _api.Agents.Respond = (_, request) => DraftAnswers.From(request);
        DraftingSites.Acme(_api.Websites);
    }

    [Fact]
    public async Task A_run_reads_the_site_drafts_the_kit_and_tells_the_admin()
    {
        var (admin, brandId, handle) = await WorkspaceAsync();
        var runId = await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        Assert.Equal(1, await DispatchAsync());

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal(runId, run.GetProperty("id").GetGuid());
        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);
        Assert.Equal(DraftingSites.PageUrls, run.GetProperty("pages").EnumerateArray().Select(page => page.GetProperty("url").GetString()));
        Assert.Equal("Acme Shoes — Running shoes for India", run.GetProperty("pages")[0].GetProperty("title").GetString());
        Assert.Empty(run.GetProperty("ads").EnumerateArray());
        Assert.DoesNotContain("https://acme.example/account", _api.Websites.Requests);

        var request = Assert.Single(_api.Agents.Requests);
        Assert.Equal("http://agents.test/brand-kit/draft", request.Url);
        Assert.Equal("Acme Shoes", request.Body.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal("INR", request.Body.GetProperty("brand").GetProperty("currency").GetString());
        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, request.Body.GetProperty("pages").EnumerateArray().Select(page => page.GetProperty("id").GetString()));
        Assert.Equal("#E4572E", request.Body.GetProperty("colourCandidates")[0].GetProperty("hex").GetString());
        Assert.Equal(DraftingSites.Logo, request.Body.GetProperty("logoCandidates")[0].GetProperty("url").GetString());

        var draft = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft");
        Assert.Equal("drafted", draft.GetProperty("origin").GetString());
        Assert.Equal(admin.UserId, draft.GetProperty("createdBy").GetGuid());
        var content = draft.GetProperty("content");
        Assert.Equal(DraftAnswers.Summary, content.GetProperty("voice").GetProperty("summary").GetString());
        var voiceSource = content.GetProperty("voice").GetProperty("sources")[0];
        Assert.Equal(("page", DraftingSites.Home), (voiceSource.GetProperty("kind").GetString(), voiceSource.GetProperty("ref").GetString()));
        Assert.Equal(new[] { "#E4572E" }, content.GetProperty("colours").EnumerateArray().Select(colour => colour.GetProperty("hex").GetString()));
        Assert.Equal(new[] { "Free returns for 30 days" }, content.GetProperty("keyMessages").EnumerateArray().Select(message => message.GetProperty("text").GetString()));
        var category = Assert.Single(content.GetProperty("productCategories").EnumerateArray());
        Assert.Equal(DraftingSites.Running, category.GetProperty("sources")[0].GetProperty("ref").GetString());
        Assert.Equal(DraftingSites.Logo, content.GetProperty("logo").GetProperty("sourceUrl").GetString());
        var logo = Assert.Single(draft.GetProperty("files").EnumerateArray());
        Assert.Equal(("logo", "image/png", "logo.png"), (logo.GetProperty("kind").GetString(), logo.GetProperty("contentType").GetString(), logo.GetProperty("fileName").GetString()));
        Assert.Equal(content.GetProperty("logo").GetProperty("fileId").GetGuid(), logo.GetProperty("id").GetGuid());

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_connectionString, brandId), row => row.Kind == "brand_kit.drafted");
        Assert.Equal(admin.UserId, notification.UserId);
        Assert.Equal("Your Brand Kit draft is ready", notification.Title);
        Assert.Equal("Kairo read 4 pages and 0 ads. Review the draft, then approve it.", notification.Body);
        Assert.Equal($"/{handle}/brand-kit", notification.Link);
    }

    [Fact]
    public async Task Drafting_is_published_live_and_audited()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();

        var events = await OutboxRows.ReadAsync(_connectionString, brandId);
        Assert.Equal(
            new[] { "pending", "running", "succeeded" },
            events.Where(row => row.Type == "brand_kit.drafting").Select(row => JsonDocument.Parse(row.Payload).RootElement.GetProperty("status").GetString()));
        Assert.Contains(events, row => row.Type == "brand_kit.changed");
        Assert.Contains(events, row => row.Type == "brand_kit.files_changed");
        var actions = await Workspaces.AuditActionsAsync(admin.Browser, brandId);
        Assert.Contains("brand_kit.drafting_started", actions);
        Assert.Contains("brand_kit.draft_saved", actions);
    }

    [Fact]
    public async Task Google_Ads_are_read_and_cited()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.BestAdRows = [FakeGoogleApis.SearchAd("7001", ["Run further"], ["Free returns for 30 days."])];
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        var ad = Assert.Single(run.GetProperty("ads").EnumerateArray());
        Assert.Equal(("7001", "Run further"), (ad.GetProperty("id").GetString(), ad.GetProperty("headline").GetString()));
        var sent = Assert.Single(Assert.Single(_api.Agents.Requests).Body.GetProperty("ads").EnumerateArray());
        Assert.Equal("a1", sent.GetProperty("id").GetString());
        var source = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").GetProperty("content").GetProperty("keyMessages")[0].GetProperty("sources")[0];
        Assert.Equal(("ad", "7001"), (source.GetProperty("kind").GetString(), source.GetProperty("ref").GetString()));
        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_connectionString, brandId), row => row.Kind == "brand_kit.drafted");
        Assert.Equal("Kairo read 4 pages and 1 ad. Review the draft, then approve it.", notification.Body);
    }

    [Fact]
    public async Task An_agent_failure_is_retried_once()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        _api.Agents.Failures = 1;
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();
        var between = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        await DispatchAsync();

        Assert.Equal("running", between.GetProperty("status").GetString());
        Assert.Equal("succeeded", (await DraftingCalls.LatestAsync(admin.Browser, brandId)).GetProperty("status").GetString());
        Assert.Equal(2, _api.Agents.Requests.Count);
    }

    [Fact]
    public async Task A_second_agent_failure_ends_the_run()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        _api.Agents.Failures = 2;
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();
        await DispatchAsync();

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("drafting_failed", run.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(FailedMessage, run.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(4, run.GetProperty("pages").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").ValueKind);
        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_connectionString, brandId), row => row.Kind == "brand_kit.drafted");
        Assert.Equal(("Kairo couldn't draft the Brand Kit", FailedMessage), (notification.Title, notification.Body));
        Assert.Equal(0, await DispatchAsync());
    }

    [Fact]
    public async Task An_answer_that_does_not_match_the_schema_counts_as_a_failure()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        _api.Agents.Respond = (_, _) => FakeAgents.Text("{\"voice\":null,\"model\":\"gemini-test\"}");
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();
        await DispatchAsync();

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal("drafting_failed", run.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _api.Agents.Requests.Count);
    }

    [Fact]
    public async Task An_unreadable_site_without_ads_ends_with_the_hand_fill_message()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        await DraftingCalls.StartedAsync(admin.Browser, brandId, Unreadable);

        await DispatchAsync();

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        var message = $"Kairo couldn't read {Unreadable}. You can fill in the Brand Kit by hand.";
        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal(("site_unreadable", message), (run.GetProperty("error").GetProperty("code").GetString(), run.GetProperty("error").GetProperty("message").GetString()));
        Assert.Empty(_api.Agents.Requests);
        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_connectionString, brandId), row => row.Kind == "brand_kit.drafted");
        Assert.Equal(message, notification.Body);
    }

    [Fact]
    public async Task Ads_alone_are_enough_to_draft()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.BestAdRows = [FakeGoogleApis.SearchAd("7001", ["Run further"], ["Free returns for 30 days."])];
        await DraftingCalls.StartedAsync(admin.Browser, brandId, Unreadable);

        await DispatchAsync();

        Assert.Equal("succeeded", (await DraftingCalls.LatestAsync(admin.Browser, brandId)).GetProperty("status").GetString());
        Assert.Equal(0, Assert.Single(_api.Agents.Requests).Body.GetProperty("pages").GetArrayLength());
        var voiceSource = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").GetProperty("content").GetProperty("voice").GetProperty("sources")[0];
        Assert.Equal(("ad", "7001"), (voiceSource.GetProperty("kind").GetString(), voiceSource.GetProperty("ref").GetString()));
    }

    [Fact]
    public async Task Replacing_a_draft_keeps_its_rules_and_guideline_files()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        var guideline = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.SaveAsync(
            admin.Browser,
            brandId,
            new { voice = new { summary = "Old voice." }, rules = new { bannedWords = new[] { "cheap" } }, guidelineFileIds = new[] { guideline } },
            0);
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home, replaceDraft: true);

        await DispatchAsync();

        var draft = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft");
        Assert.Equal((1, "drafted", 2), (draft.GetProperty("number").GetInt32(), draft.GetProperty("origin").GetString(), draft.GetProperty("revision").GetInt32()));
        var content = draft.GetProperty("content");
        Assert.Equal(DraftAnswers.Summary, content.GetProperty("voice").GetProperty("summary").GetString());
        Assert.Equal(new[] { "cheap" }, content.GetProperty("rules").GetProperty("bannedWords").EnumerateArray().Select(word => word.GetString()));
        Assert.Equal(new[] { guideline }, content.GetProperty("guidelineFileIds").EnumerateArray().Select(id => id.GetGuid()));
    }

    [Fact]
    public async Task A_new_draft_starts_from_the_approved_rules()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { rules = new { bannedWords = new[] { "cheap" } } });
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();

        var draft = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft");
        Assert.Equal(2, draft.GetProperty("number").GetInt32());
        Assert.Equal(new[] { "cheap" }, draft.GetProperty("content").GetProperty("rules").GetProperty("bannedWords").EnumerateArray().Select(word => word.GetString()));
    }

    [Fact]
    public async Task A_run_whose_lease_ran_out_twice_fails_without_calling_the_agent()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        var runId = await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);
        await ExpireLeaseAsync(runId, attempts: 2);

        await DispatchAsync();

        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal("drafting_failed", run.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_api.Agents.Requests);
    }

    [Fact]
    public async Task A_logo_that_cannot_be_downloaded_leaves_the_logo_empty()
    {
        var (admin, brandId, _) = await WorkspaceAsync();
        _api.Websites.Status(DraftingSites.Logo, HttpStatusCode.NotFound);
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        await DispatchAsync();

        var draft = (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft");
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("content").GetProperty("logo").ValueKind);
        Assert.Empty(draft.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task An_unexpected_error_frees_the_run_for_one_retry_then_fails_it()
    {
        await using var api = new KairoApiFactory(_connectionString, services => services.AddSingleton<IFileStore>(new BrokenFileStore()));
        api.Agents.Respond = (_, request) => DraftAnswers.From(request);
        DraftingSites.Acme(api.Websites);
        var admin = await Workspaces.SignInAsync(api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await DraftingCalls.StartedAsync(admin.Browser, brandId, DraftingSites.Home);

        Assert.Equal(1, await DispatchAsync(api));
        var between = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal(1, await DispatchAsync(api));

        Assert.Equal("running", between.GetProperty("status").GetString());
        var run = await DraftingCalls.LatestAsync(admin.Browser, brandId);
        Assert.Equal(("failed", "drafting_failed"), (run.GetProperty("status").GetString(), run.GetProperty("error").GetProperty("code").GetString()));
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").ValueKind);
        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_connectionString, brandId), row => row.Kind == "brand_kit.drafted");
        Assert.Equal("Kairo couldn't draft the Brand Kit", notification.Title);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private Task<int> DispatchAsync() => DispatchAsync(_api);

    private static async Task<int> DispatchAsync(KairoApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DraftDispatcher>().RunDueAsync(Token);
    }

    private async Task ExpireLeaseAsync(Guid runId, int attempts)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE brand_kit.DraftRuns SET Status = N'Running', Attempts = @attempts, ClaimedUntil = @expired WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@attempts", attempts);
        command.Parameters.AddWithValue("@expired", _api.Clock.GetUtcNow().AddMinutes(-1));
        command.Parameters.AddWithValue("@id", runId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
    }

    private sealed class BrokenFileStore : IFileStore
    {
        public Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The disk is full.");

        public Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The disk is full.");
    }

    private async Task<(Member Admin, Guid BrandId, string Handle)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        return (admin, await Workspaces.CreateAsync(admin.Browser, handle), handle);
    }
}
