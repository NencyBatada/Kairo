using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Launch;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Launch;

public sealed class PacingTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private FakePacingGoogle _google = null!;
    private Guid _room;
    private LaunchPlan _plan = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _google = new FakePacingGoogle(_launch);
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
        _room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(_room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        var record = await RecordAsync();
        Assert.Equal("live", record.Status);
        _plan = JsonSerializer.Deserialize<LaunchPlan>(record.Plan, JsonSerializerOptions.Web)!;
        var resources = JsonSerializer.Deserialize<Dictionary<string, string>>(record.Resources, JsonSerializerOptions.Web)!;
        foreach (var channel in _plan.Settings.Channels) _google.Campaigns[channel.CampaignType] = resources["campaign:" + channel.CampaignType];
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task A_campaign_on_plan_is_recorded_without_alerts()
    {
        var first = FirstDay();
        Spend(first, 1.1m);
        Spend(first.AddDays(1), 0.85m);
        At(first.AddDays(2), 7);
        Assert.Equal(1, await PacingAsync());
        var view = await ViewAsync();
        Assert.Equal("on_pace", view.GetProperty("status").GetString());
        var campaign = view.GetProperty("campaigns")[0];
        Assert.Equal(2, campaign.GetProperty("days").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, campaign.GetProperty("alert").ValueKind);
        Assert.DoesNotContain(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "pacing.alert");
    }

    [Fact]
    public async Task Two_days_off_plan_alert_once_per_streak_with_the_likely_cause_and_again_after_a_direction_change()
    {
        var first = FirstDay();
        _google.PausedAds = 2;
        Spend(first, 0.5m);
        At(first.AddDays(1), 7);
        Assert.Equal(1, await PacingAsync());
        Assert.Empty(await AlertsAsync());

        Spend(first.AddDays(1), 0.6m);
        At(first.AddDays(2), 7);
        Assert.Equal(1, await PacingAsync());
        var alerts = await AlertsAsync();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, row => Assert.Contains("under plan for 2 days in a row", row.Body, StringComparison.Ordinal));
        Assert.All(alerts, row => Assert.Contains("2 of 4 ads are paused or disapproved", row.Body, StringComparison.Ordinal));
        Assert.All(alerts, row => Assert.EndsWith($"/campaigns/{_room}/performance", row.Link, StringComparison.Ordinal));
        var view = await ViewAsync();
        Assert.Equal("off_pace", view.GetProperty("status").GetString());
        var alert = view.GetProperty("campaigns").EnumerateArray().Select(campaign => campaign.GetProperty("alert")).First(item => item.ValueKind != JsonValueKind.Null);
        Assert.Equal(("under", "ads_unavailable"), (alert.GetProperty("direction").GetString(), alert.GetProperty("causeKind").GetString()));
        Assert.NotEmpty(alert.GetProperty("suggestedFix").GetString()!);

        Spend(first.AddDays(2), 0.5m);
        At(first.AddDays(3), 7);
        Assert.Equal(1, await PacingAsync());
        Assert.Equal(alerts.Length, (await AlertsAsync()).Length);

        _google.PausedAds = 0;
        Spend(first.AddDays(3), 1.5m, clicks: 100);
        At(first.AddDays(4), 7);
        Assert.Equal(1, await PacingAsync());
        Assert.Equal(alerts.Length, (await AlertsAsync()).Length);
        Spend(first.AddDays(4), 1.6m, clicks: 100);
        At(first.AddDays(5), 7);
        Assert.Equal(1, await PacingAsync());
        var latest = (await AlertsAsync()).Where(row => !alerts.Any(old => old.Id == row.Id)).ToArray();
        Assert.NotEmpty(latest);
        Assert.All(latest, row => Assert.Contains("over plan for 2 days in a row", row.Body, StringComparison.Ordinal));
        Assert.All(latest, row => Assert.Contains("Clicks cost", row.Body, StringComparison.Ordinal));
        Assert.Equal(2, await PacingAuditsAsync());
    }

    [Fact]
    public async Task Under_pace_from_Kairo_stock_pauses_says_so()
    {
        var record = await RecordAsync();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
            foreach (var (type, campaign) in _google.Campaigns)
                foreach (var index in Enumerable.Range(0, 2))
                    db.AdPauses.Add(new AdPause { Id = Guid.NewGuid(), RoomId = _room, LaunchId = record.Id, CampaignType = type, CampaignResource = campaign,
                        AdResource = $"{campaign}/ads/{index}", State = "paused", CreatedAt = _world.Api.Clock.GetUtcNow(), PausedAt = _world.Api.Clock.GetUtcNow() });
            db.AdPauses.Add(new AdPause { Id = Guid.NewGuid(), RoomId = _room, LaunchId = record.Id, CampaignType = _google.Campaigns.Keys.First(), CampaignResource = "held",
                AdResource = "held/ads/9", State = "held", CreatedAt = _world.Api.Clock.GetUtcNow() });
            await db.SaveChangesAsync(Token);
        }
        _google.PausedAds = 2;
        var first = FirstDay();
        Spend(first, 0.5m);
        Spend(first.AddDays(1), 0.5m);
        At(first.AddDays(2), 7);
        Assert.Equal(1, await PacingAsync());
        var alerts = await AlertsAsync();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, row => Assert.Contains("Kairo paused 2 of 4 ads because their products sold out", row.Body, StringComparison.Ordinal));
        var alert = (await ViewAsync()).GetProperty("campaigns")[0].GetProperty("alert");
        Assert.Equal("stock_paused", alert.GetProperty("causeKind").GetString());
    }

    [Fact]
    public async Task Alerts_go_to_marketers_and_budget_owners_only()
    {
        var bystander = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        var first = FirstDay();
        Spend(first, 2m);
        Spend(first.AddDays(1), 2m);
        At(first.AddDays(2), 7);
        Assert.Equal(1, await PacingAsync());
        var recipients = (await AlertsAsync()).Select(row => row.UserId).ToHashSet();
        Assert.Contains(_world.Brand.Marketer.UserId, recipients);
        Assert.Contains(_world.Approver.UserId, recipients);
        Assert.DoesNotContain(bystander.UserId, recipients);
    }

    [Fact]
    public async Task Days_wait_for_settled_data_catch_up_together_and_reruns_record_nothing_new()
    {
        var first = FirstDay();
        foreach (var offset in Enumerable.Range(0, 3)) Spend(first.AddDays(offset), 0.4m);
        At(first.AddDays(3), 5);
        Assert.Equal(1, await PacingAsync());
        Assert.Equal(2, await DaysAsync());
        At(first.AddDays(3), 6);
        Assert.Equal(1, (await Task.WhenAll(PacingAsync(), PacingAsync())).Sum());
        Assert.Equal(3, await DaysAsync());
        Assert.Equal(0, await PacingAsync());
        var alerts = await AlertsAsync();
        Assert.Equal(alerts.Length, alerts.DistinctBy(row => row.UserId).Count());
        Assert.Equal(1, await PacingAuditsAsync());
    }

    [Fact]
    public async Task A_failed_lookup_records_nothing_and_the_next_run_fills_the_day()
    {
        var first = FirstDay();
        Spend(first, 1m);
        At(first.AddDays(1), 7);
        _launch.TransientReads = 1;
        _world.Api.GoogleApis.LaunchResponse = (uri, body) => _launch.TransientReads > 0 ? _launch.Respond(uri, body) : _google.Respond(uri, body);
        Assert.Equal(0, await PacingAsync());
        Assert.Equal(0, await DaysAsync());
        Assert.Equal(1, await PacingAsync());
        Assert.Equal(1, await DaysAsync());
    }

    [Fact]
    public async Task Pacing_is_scoped_to_the_workspace()
    {
        var first = FirstDay();
        Spend(first, 1m);
        At(first.AddDays(1), 7);
        await PacingAsync();
        using var foreign = await _world.Marketer.GetAsync($"/api/brands/{Guid.NewGuid()}/campaigns/{_room}/pacing", Token);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var missing = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{Guid.NewGuid()}/pacing", Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Pacing.ToListAsync(Token));
    }

    [Fact]
    public async Task The_worker_job_runs_the_pacing_check()
    {
        var first = FirstDay();
        Spend(first, 1m);
        At(first.AddDays(1), 7);
        await using var worker = new KairoWorkerFactory(_world.Database, _world.Api.GoogleApis, _world.Api.Clock.GetUtcNow());
        using var client = worker.CreateClient();
        using var response = await client.PostAsync("/jobs/launches/pacing", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("processed").GetInt32());
    }

    [Fact]
    public async Task The_additive_migration_keeps_live_launches_and_pacing_starts_after_it()
    {
        var original = await RecordAsync();
        await using (var scope = await ScopeAsync())
        {
            var migrator = scope.ServiceProvider.GetRequiredService<LaunchDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261016123611_StockPauses", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
        }
        var record = await RecordAsync();
        Assert.Equal((original.Plan, original.Resources, "live"), (record.Plan, record.Resources, record.Status));
        var first = FirstDay();
        Spend(first, 1m);
        At(first.AddDays(1), 7);
        Assert.Equal(1, await PacingAsync());
        Assert.Equal(1, await DaysAsync());
    }

    private DateOnly FirstDay()
    {
        var launchDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_world.Api.Clock.GetUtcNow(), Zone).DateTime);
        return _plan.Start > launchDay ? _plan.Start : launchDay.AddDays(1);
    }

    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(_plan.TimeZone);

    private void At(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        _world.Api.Clock.SetUtcNow(new DateTimeOffset(local, Zone.GetUtcOffset(local)));
    }

    private void Spend(DateOnly date, decimal share, long clicks = 1000)
    {
        foreach (var channel in _plan.Settings.Channels)
            _google.Days[(channel.CampaignType, date)] = (decimal.Round(PacingRules.PlannedDaily(channel, _plan) * share, 2), clicks);
    }

    private async Task<int> PacingAsync() { await using var scope = _world.Api.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<PacingCheck>().RunDueAsync(Token); }
    private async Task<NotificationRow[]> AlertsAsync() => [.. (await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId)).Where(row => row.Kind == "pacing.alert")];
    private async Task<JsonElement> ViewAsync()
    {
        Assert.Equal("/", await _world.Api.SignInAsync(_world.Marketer, _world.Brand.Marketer.Account, "/"));
        return await _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{_room}/pacing", Token);
    }
    private async Task<int> PacingAuditsAsync() { await using var scope = await ScopeAsync(); return (await scope.ServiceProvider.GetRequiredService<IAuditTrail>().ForTargetAsync("campaign_room", _room.ToString(), null, 200, Token)).Entries.Count(entry => entry.Action == "launch.pacing_alerted"); }
    private async Task<int> DaysAsync() { await using var scope = await ScopeAsync(); return await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Pacing.Select(row => row.Date).Distinct().CountAsync(Token); }
    private async Task<AsyncServiceScope> ScopeAsync() { var scope = _world.Api.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token); return scope; }
    private async Task<LaunchRecord> RecordAsync() { await using var scope = await ScopeAsync(); return await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.AsNoTracking().SingleAsync(row => row.RoomId == _room && row.Status != "superseded", Token); }
}
