using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Launch;
using Kairo.Modules.Launch;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lift;
using Kairo.SharedKernel.Agents;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Measurement;

public sealed class LiftReadingTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private readonly FakeLift _lift = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _world.Api.GoogleApis.LaunchResponse = _launch.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
        _lift.Attach(_world.Worker.Agents);
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Early_readings_start_after_seven_covered_days_and_come_once_per_data_day()
    {
        var (room, test) = await LiveAsync();
        await AdvanceToAsync(test.Start!.Value.AddDays(7));
        await CoverAsync(test.Start.Value.AddDays(5));
        await RunAsync();
        Assert.Empty(await ReadingsAsync(room));

        await CoverAsync(test.Start.Value.AddDays(6));
        await RunAsync();
        await RunAsync();
        var first = Assert.Single(await ReadingsAsync(room));
        Assert.True(first.Early);
        Assert.Equal((test.Start.Value.AddDays(6), 7, "ok"), (first.ThroughDate, first.Days, first.Status));
        var request = Assert.Single(_lift.Requests);
        Assert.Equal(("orders", 7, LiftAgent.HistoryDays, LiftAgent.HistoryDays + 7), (request.GetProperty("kpi").GetString(),
            request.GetProperty("testDays").GetInt32(), request.GetProperty("start").GetInt32(), request.GetProperty("days").GetInt32()));
        var regions = request.GetProperty("regions").EnumerateArray().ToList();
        Assert.Contains(regions, region => region.GetProperty("test").GetBoolean());
        Assert.Contains(regions, region => !region.GetProperty("test").GetBoolean());
        Assert.All(regions.Where(region => region.GetProperty("outcome").ValueKind == JsonValueKind.Array),
            region => Assert.Equal(LiftAgent.HistoryDays + 7, region.GetProperty("outcome").GetArrayLength()));

        await AdvanceToAsync(test.Start.Value.AddDays(8));
        await CoverAsync(test.Start.Value.AddDays(7));
        await RunAsync();
        Assert.Equal(2, (await ReadingsAsync(room)).Count);

        var view = await ViewAsync(room);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("final").ValueKind);
        var latest = view.GetProperty("latestEarly");
        Assert.Equal(Campaigns.Iso(test.Start.Value.AddDays(7)), latest.GetProperty("throughDate").GetString());
        Assert.True(latest.GetProperty("early").GetBoolean());
        Assert.Equal(0.12, latest.GetProperty("lift").GetDouble(), 3);
        Assert.Equal(8, latest.GetProperty("daily").GetArrayLength());
        Assert.Equal(Campaigns.Iso(test.Start.Value), latest.GetProperty("daily")[0].GetProperty("date").GetString());
        Assert.Contains(latest.GetProperty("regions").EnumerateArray(), region => !region.GetProperty("test").GetBoolean());
        Assert.Equal(2, view.GetProperty("readings").GetArrayLength());
        Assert.Equal(2, (await AuditAsync("measurement.lift_measured")).Length);
    }

    [Fact]
    public async Task The_final_reading_waits_for_data_covering_the_test_end()
    {
        var (room, test) = await LiveAsync();
        var end = test.End!.Value;
        await AdvanceToAsync(end.AddDays(1));
        await RunJobAsync("/jobs/campaigns/test-ends");
        await CoverAsync(end.AddDays(-1));
        await RunAsync();
        Assert.DoesNotContain(await ReadingsAsync(room), reading => !reading.Early);

        await CoverAsync(end);
        await RunAsync();
        await RunAsync();
        var final = Assert.Single(await ReadingsAsync(room), reading => !reading.Early);
        var planned = end.DayNumber - test.Start!.Value.DayNumber + 1;
        Assert.Equal((end, planned, planned), (final.ThroughDate, final.Days, final.PlannedDays));
        Assert.Equal("results_ready", (await TestAsync(room)).Stage);
        Assert.Equal(0.12, JsonDocument.Parse(final.Revenue!).RootElement.GetProperty("lift").GetDouble(), 3);
    }

    [Fact]
    public async Task An_early_end_measures_only_the_full_days_before_it()
    {
        var (room, test) = await LiveAsync();
        await AdvanceToAsync(test.Start!.Value.AddDays(9));
        await EndEarlyAsync(room);
        await CoverAsync(test.Start.Value.AddDays(9));
        await RunAsync();
        var final = Assert.Single(await ReadingsAsync(room));
        Assert.False(final.Early);
        Assert.Equal((test.Start.Value.AddDays(8), 9), (final.ThroughDate, final.Days));
        Assert.Equal(test.End!.Value.DayNumber - test.Start.Value.DayNumber + 1, final.PlannedDays);
    }

    [Fact]
    public async Task Data_that_never_catches_up_still_gets_a_final_reading_a_week_later()
    {
        var (room, test) = await LiveAsync();
        var end = test.End!.Value;
        await AdvanceToAsync(end.AddDays(1));
        await RunJobAsync("/jobs/campaigns/test-ends");
        await CoverAsync(end.AddDays(-3));
        await AdvanceToAsync(end.AddDays(7));
        await RunAsync();
        Assert.DoesNotContain(await ReadingsAsync(room), reading => !reading.Early);

        await AdvanceToAsync(end.AddDays(8));
        await RunAsync();
        var final = Assert.Single(await ReadingsAsync(room), reading => !reading.Early);
        Assert.Equal(end.AddDays(-3), final.ThroughDate);
        Assert.True(final.Days < final.PlannedDays);
    }

    [Fact]
    public async Task Thin_data_is_saved_as_insufficient_and_agent_failures_retry_next_run()
    {
        var (room, test) = await LiveAsync();
        await AdvanceToAsync(test.Start!.Value.AddDays(8));
        await CoverAsync(test.Start.Value.AddDays(7));
        _lift.Fails = true;
        await RunAsync();
        Assert.Empty(await ReadingsAsync(room));

        _lift.Fails = false;
        _lift.Status = "insufficient_data";
        await RunAsync();
        var reading = Assert.Single(await ReadingsAsync(room));
        Assert.Equal(("insufficient_data", "few_placebos", false), (reading.Status, reading.Reason, reading.Conclusive));
        Assert.Null(reading.Lift);
        var view = await ViewAsync(room);
        Assert.Equal("few_placebos", view.GetProperty("latestEarly").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Readings_stay_inside_their_workspace()
    {
        var (room, test) = await LiveAsync();
        await AdvanceToAsync(test.Start!.Value.AddDays(8));
        await CoverAsync(test.Start.Value.AddDays(7));
        await RunAsync();
        Assert.Single(await ReadingsAsync(room));

        using var foreign = await _world.Marketer.GetAsync($"/api/brands/{Guid.NewGuid()}/campaigns/{room}/lift", Token);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var missing = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{Guid.NewGuid()}/lift", Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().LiftReadings.ToListAsync(Token));
    }

    [Fact]
    public async Task The_additive_migration_keeps_feed_runs_and_readings_start_after_it()
    {
        var (room, test) = await LiveAsync();
        await CoverAsync(test.Start!.Value.AddDays(7));
        await using (var scope = await ScopeAsync())
        {
            var migrator = scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261015165054_FeedRunTables", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
        }

        await using (var scope = await ScopeAsync())
            Assert.Single(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().FeedRuns.ToListAsync(Token));
        await AdvanceToAsync(test.Start.Value.AddDays(8));
        await RunAsync();
        Assert.Single(await ReadingsAsync(room));
    }

    private async Task<(Guid Room, CampaignTest Test)> LiveAsync()
    {
        var room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        Assert.Equal("live", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
        return (room, await TestAsync(room));
    }

    private async Task<CampaignTest> TestAsync(Guid room)
    {
        await using var scope = await ScopeAsync();
        return (await scope.ServiceProvider.GetRequiredService<ICampaignTests>().GetAsync(room, Token))!;
    }

    private async Task CoverAsync(DateOnly through)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<MeasurementDbContext>();
        db.FeedRuns.RemoveRange(await db.FeedRuns.ToListAsync(Token));
        db.FeedRuns.Add(new FeedRun
        {
            Id = Guid.NewGuid(), Feed = FeedNames.SalesTable, Status = RunStatuses.Succeeded, FromDate = through.AddDays(-7), ToDate = through,
            StartedAt = _world.Api.Clock.GetUtcNow(), FinishedAt = _world.Api.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(Token);
    }

    private async Task<List<LiftReading>> ReadingsAsync(Guid room)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().LiftReadings.AsNoTracking()
            .Where(row => row.RoomId == room).OrderBy(row => row.ThroughDate).ToListAsync(Token);
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return scope;
    }

    private async Task EndEarlyAsync(Guid room)
    {
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        using var response = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, room, "end-test", new { version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task AdvanceToAsync(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(5.5));
        while (_world.Api.Clock.GetUtcNow() < target)
        {
            var next = _world.Api.Clock.GetUtcNow() + TimeSpan.FromDays(1);
            var step = next < target ? next : target;
            _world.Api.Clock.SetUtcNow(step);
            if (_world.Worker.Clock.GetUtcNow() < step) _world.Worker.Clock.SetUtcNow(step);
            foreach (var browser in new[] { _world.Marketer, _world.Admin, _world.Approver.Browser })
                await browser.GetAsync($"/api/brands/{_world.BrandId}", Token);
        }
    }

    private Task RunAsync() => RunJobAsync("/jobs/measurement/lift");

    private async Task RunJobAsync(string path)
    {
        using var client = _world.Worker.CreateClient();
        using var response = await client.PostAsync(path, null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<JsonElement> ViewAsync(Guid room) =>
        _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{room}/lift", Token);

    private async Task<JsonElement[]> AuditAsync(string action) =>
        [.. (await _world.Admin.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/audit-log?action={action}", Token)).GetProperty("entries").EnumerateArray()];
}
