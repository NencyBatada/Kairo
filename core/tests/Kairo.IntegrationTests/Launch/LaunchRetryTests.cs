using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Launch;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.BrandKit;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Launch;

public sealed class LaunchRetryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _google = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
        _world.Api.GoogleApis.LaunchRequest = _google.RespondRequest;
    }
    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Transient_errors_use_bounded_durable_backoff()
    {
        var room = await QueuedAsync();
        _google.TransientReads = 5;
        await RunAsync();
        Assert.Equal("retry_wait", (await RecordAsync(room)).Status);
        Assert.Equal(0, await RunAsync());
        foreach (var seconds in new[] { 30, 120, 600 }) { _world.Api.Clock.Advance(TimeSpan.FromSeconds(seconds)); Assert.Equal(1, await RunAsync()); }
        var failed = await RecordAsync(room);
        Assert.Equal(("failed", 4, 3), (failed.Status, failed.Attempts, failed.RetryCount));
        Assert.Null(failed.NextAttemptAt);
        Assert.Empty(_google.Resources);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("activation")]
    [InlineData("timeout")]
    public async Task Lost_build_or_activation_responses_reconcile_without_duplicate_creates_or_activation(string outcome)
    {
        var room = await QueuedAsync();
        _google.LoseBuildResponse = outcome == "build";
        _google.LoseActivationResponse = outcome == "activation";
        _google.TimeoutBuild = outcome == "timeout";
        await RunAsync();
        Assert.True((await RecordAsync(room)).Uncertain);
        var count = _google.Resources.Count;
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync();
        var completed = await RecordAsync(room);
        Assert.True(completed.Status == "live", completed.ErrorMessage);
        Assert.Equal(count, _google.Resources.Count);
        Assert.Single(Writes(), Build);
        Assert.Single(Writes(), Activation);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        Assert.Contains(await db.Steps.Where(row => row.LaunchId == completed.Id).ToListAsync(Token), step => step.Outcome == "reconciled");
        Assert.NotEmpty(await db.ProductAds.Where(row => row.RoomId == room).ToListAsync(Token));
    }

    [Fact]
    public async Task A_mixed_activation_outcome_is_held_for_attention_without_enabling_again()
    {
        var room = await QueuedAsync();
        _google.LoseActivationResponse = true;
        await RunAsync();
        _google.Resources.First(pair => pair.Key.Contains("/campaigns/", StringComparison.Ordinal)).Value["status"] = "PAUSED";
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync();
        var record = await RecordAsync(room);
        Assert.Equal(("failed", "reconciliation_required", "activate"), (record.Status, record.ErrorCode, record.AttemptingStep));
        Assert.True(record.Uncertain);
        Assert.Single(Writes(), Activation);
    }

    [Fact]
    public async Task An_expired_lease_is_claimed_by_a_new_worker_and_confirmed_steps_resume()
    {
        var room = await QueuedAsync();
        _google.FailActivation = true;
        await RunAsync();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
            var record = await db.Launches.SingleAsync(row => row.RoomId == room, Token);
            record.Status = "running";
            record.ClaimToken = Guid.NewGuid();
            record.ClaimedUntil = _world.Api.Clock.GetUtcNow() + TimeSpan.FromMinutes(10);
            await db.SaveChangesAsync(Token);
        }
        Assert.Equal(0, await RunAsync());
        _google.FailActivation = false;
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(11));
        await using var restarted = new KairoWorkerFactory(_world.Database, _world.Api.GoogleApis, _world.Api.Clock.GetUtcNow());
        using var workerClient = restarted.CreateClient();
        var sweep = workerClient.PostAsync("/jobs/launches/run", null, Token);
        var localCount = await RunAsync();
        using var response = await sweep;
        Assert.Equal(1, localCount + (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("processed").GetInt32());
        Assert.Equal("live", (await RecordAsync(room)).Status);
        Assert.Single(Writes(), Build);
    }

    [Fact]
    public async Task Unresolvable_outcomes_require_attention_and_never_resubmit_the_create()
    {
        var room = await QueuedAsync();
        _google.LoseBuildResponse = true;
        await RunAsync();
        _google.HideResources = true;
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync();
        var failed = await RecordAsync(room);
        Assert.Equal(("failed", "reconciliation_required", "build"), (failed.Status, failed.ErrorCode, failed.AttemptingStep));
        Assert.True(failed.Uncertain);
        Assert.Single(Writes(), Build);
        Assert.DoesNotContain(Writes(), Activation);
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, room);
        using var returned = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, room, "send-back", new { to = "ready_to_launch", comment = "Fix the launch", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.Conflict, returned.StatusCode);
    }

    [Fact]
    public async Task Invalidated_approval_never_erases_an_uncertain_external_intent()
    {
        var room = await QueuedAsync();
        _google.LoseBuildResponse = true;
        await RunAsync();
        await using (var scope = await ScopeAsync()) await scope.ServiceProvider.GetRequiredService<IApprovalLocks>().InvalidateAsync(room, ["launch"], Token);
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync();
        var record = await RecordAsync(room);
        Assert.Equal(("approval_changed", "build"), (record.ErrorCode, record.AttemptingStep));
        Assert.True(record.Uncertain);
        Assert.Single(Writes(), Build);
        Assert.DoesNotContain(Writes(), Activation);
    }

    [Fact]
    public async Task Marketer_retry_is_versioned_tenant_scoped_and_requires_the_approved_snapshot()
    {
        var room = await QueuedAsync();
        _google.FailActivation = true;
        await RunAsync();
        var record = await RecordAsync(room);
        var path = Path(room) + "/retry";
        using var denied = await _world.Approver.Browser.PostAsJsonAsync(path, new { record.Version }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        _google.FailActivation = false;
        var retries = await Task.WhenAll(_world.Marketer.PostAsJsonAsync(path, new { record.Version }, Token), _world.Marketer.PostAsJsonAsync(path, new { record.Version }, Token));
        Assert.Single(retries, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(retries, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in retries) response.Dispose();
        await using (var scope = await ScopeAsync()) await scope.ServiceProvider.GetRequiredService<IApprovalLocks>().InvalidateAsync(room, ["launch"], Token);
        await RunAsync();
        record = await RecordAsync(room);
        using var stale = await _world.Marketer.PostAsJsonAsync(path, new { record.Version }, Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.DoesNotContain(Writes(), Activation);
        using var foreign = await _world.Marketer.GetAsync($"/api/brands/{Guid.NewGuid()}/campaigns/{room}/launch", Token);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task A_known_failed_launch_can_return_for_fresh_approval_while_preserving_its_snapshot()
    {
        var room = await QueuedAsync();
        _google.MerchantLinked = false;
        await RunAsync();
        var original = await RecordAsync(room);
        var detail = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, room);
        using var returned = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, room, "send-back", new { to = "ready_to_launch", comment = "Correct the launch settings", version = Campaigns.Version(detail) });
        Assert.True(returned.IsSuccessStatusCode, await returned.Content.ReadAsStringAsync(Token));
        _google.MerchantLinked = true;
        await _world.LaunchPreparedAsync(room);
        await RunAsync();
        Assert.Equal("live", (await RecordAsync(room)).Status);
        await using var scope = await ScopeAsync();
        var history = await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.AsNoTracking().SingleAsync(row => row.Id == original.Id, Token);
        Assert.Equal("superseded", history.Status);
        Assert.Equal(original.Plan, history.Plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resumable_video_uploads_resume_the_confirmed_offset_or_recover_the_completed_video(bool completed)
    {
        await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var logo = await BrandKits.UploadAsync(_world.Admin, _world.BrandId, BrandKits.Png, "logo.png", "logo");
        await BrandKits.ApproveNewAsync(_world.Admin, _world.BrandId, new { logo = new { fileId = logo }, colours = new[] { new { hex = "#003366", name = "Navy", role = "primary" } } });
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "shopping", "demand_gen" });
            return draft;
        };
        var room = await QueuedAsync();
        _google.InterruptUpload = !completed;
        _google.LoseUploadResponse = completed;
        await RunAsync();
        var status = await _world.Marketer.GetStringAsync(Path(room), Token);
        Assert.DoesNotContain("https://www.googleapis.com/upload-session", status);
        Assert.DoesNotContain("\"session\"", status);
        Assert.DoesNotContain("googleapis.com", status);
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync();
        var record = await RecordAsync(room);
        Assert.True(record.Status == "live", record.ErrorMessage);
        Assert.Equal(1, _google.UploadSessions);
        Assert.Equal(1, _google.UploadStatusQueries);
        Assert.Equal(completed ? 1 : 2, _google.VideoUploads);
        Assert.Equal(completed ? 0 : 2, _google.LastUploadOffset);
    }

    [Fact]
    public async Task The_additive_migration_preserves_existing_live_launches_and_schedules_their_health_check()
    {
        var room = await QueuedAsync();
        await RunAsync();
        var original = await RecordAsync(room);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016111711_CampaignLaunches", Token);
        await migrator.MigrateAsync(cancellationToken: Token);
        var record = await db.Launches.AsNoTracking().SingleAsync(row => row.Id == original.Id, Token);
        Assert.Equal((original.Plan, original.Hash, original.Resources, "live"), (record.Plan, record.Hash, record.Resources, record.Status));
        var health = await db.Health.SingleAsync(row => row.LaunchId == record.Id, Token);
        Assert.Equal(original.ActivatedAt!.Value + TimeSpan.FromHours(24), health.DueAt);
    }

    [Fact]
    public async Task Health_check_restarts_do_not_duplicate_notifications_after_a_failed_checkpoint()
    {
        var room = await QueuedAsync();
        await RunAsync();
        var original = await RecordAsync(room);
        var plan = JsonSerializer.Deserialize<LaunchPlan>(original.Plan, JsonSerializerOptions.Web)!;
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        using var amended = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, room, "proof-plan/changes", new { version = Campaigns.Version(detail), reason = "Finish measurement a day earlier", endDate = Campaigns.Iso(plan.End.AddDays(-1)) });
        Assert.True(amended.IsSuccessStatusCode, await amended.Content.ReadAsStringAsync(Token));
        Assert.Equal(original.Plan, (await RecordAsync(room)).Plan);
        Assert.Equal(0, await RunAsync());
        _google.Disapproved = true;
        _world.Api.Clock.Advance(TimeSpan.FromHours(24));
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE launch.Health ADD CONSTRAINT FailHealthCheckpoint CHECK (CheckedAt IS NULL)";
        await command.ExecuteNonQueryAsync(Token);
        await HealthAsync();
        Assert.DoesNotContain(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "health.problem");
        Assert.Equal(0, await HealthAsync());
        command.CommandText = "ALTER TABLE launch.Health DROP CONSTRAINT FailHealthCheckpoint";
        await command.ExecuteNonQueryAsync(Token);
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await HealthAsync());
        var notifications = (await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId)).Where(row => row.Kind == "health.problem").ToArray();
        Assert.NotEmpty(notifications);
        Assert.Equal(notifications.Length, notifications.DistinctBy(row => (row.UserId, row.Subject)).Count());
        Assert.Equal(0, await HealthAsync());
    }

    [Fact]
    public async Task A_transient_health_lookup_retries_after_backoff_without_sending_early_alerts()
    {
        var room = await QueuedAsync();
        await RunAsync();
        _google.TransientReads = 1;
        _world.Api.Clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(1, await HealthAsync());
        Assert.Equal(0, await HealthAsync());
        Assert.DoesNotContain(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "health.problem");
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await HealthAsync());
        var status = await _world.Marketer.GetFromJsonAsync<JsonElement>(Path(room), Token);
        Assert.Equal("complete", status.GetProperty("health").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_checks_wait_twenty_four_hours_and_alert_once_with_actionable_policy_and_delivery_reasons()
    {
        var room = await QueuedAsync();
        await RunAsync();
        _google.Disapproved = true;
        _google.Impressions = 0;
        _world.Api.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, await HealthAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await Task.WhenAll(HealthAsync(), HealthAsync())).Sum());
        var status = await _world.Marketer.GetFromJsonAsync<JsonElement>(Path(room), Token);
        Assert.Equal("complete", status.GetProperty("health").GetProperty("status").GetString());
        var alerts = status.GetProperty("health").GetProperty("alerts").EnumerateArray().ToArray();
        Assert.Contains(alerts, alert => alert.GetProperty("kind").GetString() == "delivery");
        Assert.Contains(alerts, alert => alert.GetProperty("kind").GetString() == "disapproval" && alert.GetProperty("cause").GetString()!.Contains("destination not working", StringComparison.Ordinal));
        Assert.All(alerts, alert => Assert.NotEmpty(alert.GetProperty("suggestedFix").GetString()!));
        var before = (await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId)).Count(row => row.Kind == "health.problem");
        Assert.True(before > 0);
        Assert.Equal(0, await HealthAsync());
        Assert.Equal(before, (await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId)).Count(row => row.Kind == "health.problem"));
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        Assert.Empty(await db.Health.ToListAsync(Token));
        Assert.Empty(await db.Steps.ToListAsync(Token));
    }

    private string Path(Guid room) => $"{Campaigns.Base(_world.BrandId)}/{room}/launch";
    private async Task<Guid> QueuedAsync() { var room = await _world.OpenWithBriefAsync(); await _world.LaunchAsync(room); return room; }
    private async Task<int> RunAsync() { await using var scope = _world.Api.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token); }
    private async Task<int> HealthAsync() { await using var scope = _world.Api.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<LaunchHealthCheck>().RunDueAsync(Token); }
    private async Task<AsyncServiceScope> ScopeAsync() { var scope = _world.Api.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token); return scope; }
    private async Task<LaunchRecord> RecordAsync(Guid room) { await using var scope = await ScopeAsync(); return await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.AsNoTracking().SingleAsync(row => row.RoomId == room && row.Status != "superseded", Token); }
    private JsonElement[] Writes() => _google.Mutations.Where(call => !call.GetProperty("validateOnly").GetBoolean()).ToArray();
    private static bool Build(JsonElement call) => call.GetProperty("mutateOperations").EnumerateArray().Any(op => op.TryGetProperty("campaignOperation", out var campaign) && campaign.TryGetProperty("create", out _));
    private static bool Activation(JsonElement call) => call.GetProperty("mutateOperations").EnumerateArray().Any(op => op.TryGetProperty("campaignOperation", out var campaign) && campaign.TryGetProperty("update", out _));
}
