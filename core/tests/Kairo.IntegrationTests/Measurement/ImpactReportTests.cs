using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Measurement;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Care;
using Kairo.IntegrationTests.Launch;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Impact;
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Measurement;

public sealed class ImpactReportTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private FakeCareGoogle _google = null!;
    private readonly FakeLift _lift = new();
    private readonly FakeReport _report = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _google = new FakeCareGoogle(_launch);
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
        _lift.Attach(_world.Worker.Agents);
        _report.Attach(_world.Worker.Agents);
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task A_conclusive_lift_scales_up_and_tells_marketers_and_approvers()
    {
        var (room, test) = await LiveAsync();
        Assert.Equal("measuring", (await ViewAsync(room)).GetProperty("state").GetString());
        await FinishAsync(room, test);

        var view = await ViewAsync(room);
        Assert.Equal("ready", view.GetProperty("state").GetString());
        var report = view.GetProperty("report");
        Assert.Equal(("worked", "scale_up", "INR"), (Text(report, "outcome"), Text(report, "nextStep"), Text(report, "currency")));
        Assert.Equal((2800m, 70m, 40m), (report.GetProperty("spend").GetDecimal(), report.GetProperty("results").GetDecimal(), report.GetProperty("costPerResult").GetDecimal()));
        var days = test.End!.Value.DayNumber - test.Start!.Value.DayNumber + 1;
        Assert.Equal(decimal.Round(2800m / (12m * days), 2), report.GetProperty("costPerExtra").GetDecimal());
        Assert.Equal("Outcome worked for " + test.Title + ".", Text(report, "summary"));
        Assert.Equal(("gemini-pro-test", "prove-report/1"), (Text(report, "model"), Text(report, "promptVersion")));
        Assert.Equal(JsonValueKind.Null, report.GetProperty("nextTest").ValueKind);
        Assert.Empty(report.GetProperty("flags").EnumerateArray());
        Assert.False(view.GetProperty("final").GetProperty("early").GetBoolean());

        var request = Assert.Single(_report.Requests);
        Assert.Equal((test.Title, "orders", "worked", "scale_up"), (Text(request, "title"), Text(request, "kpi"), Text(request, "outcome"), Text(request, "nextStep")));
        Assert.Equal((2800m, 0.12), (request.GetProperty("spend").GetDecimal(), request.GetProperty("lift").GetDouble()));
        Assert.NotEmpty(request.GetProperty("regions").EnumerateArray());

        var notices = (await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId)).Where(row => row.Kind == "report.ready").ToList();
        Assert.All(notices, notice => Assert.Equal(($"impact:{room:N}", $"/{_world.Brand.Handle}/campaigns/{room}/results"), (notice.Subject, notice.Link)));
        Assert.Contains(notices, notice => notice.UserId == _world.Brand.Marketer.UserId);
        Assert.Contains(notices, notice => notice.UserId == _world.Approver.UserId);
        Assert.Single(await AuditAsync("measurement.report_ready"));
    }

    [Fact]
    public async Task Reports_are_written_once_and_shared_with_learning()
    {
        var (room, test) = await LiveAsync();
        var since = _world.Api.Clock.GetUtcNow();
        await FinishAsync(room, test);
        await RunAsync();
        await RunAsync();

        Assert.Single(_report.Requests);
        Assert.Single(await AuditAsync("measurement.report_ready"));
        await using var scope = await ScopeAsync();
        var reports = scope.ServiceProvider.GetRequiredService<IImpactReports>();
        var summary = Assert.Single(await reports.ListReadyAsync(since, Token));
        Assert.Equal((room, _world.BrandId, test.Title, "worked", "scale_up"), (summary.RoomId, summary.BrandId, summary.Title, summary.Outcome, summary.NextStep));
        Assert.Equal((test.Start!.Value, test.End!.Value, 2800m), (summary.StartDate, summary.ThroughDate, summary.Spend));
        Assert.Equal((0.12, 0.06, 0.18), (summary.Lift, summary.Low, summary.High));
        Assert.Contains(summary.Regions, region => region.Test && region.Lift == 0.12);
        Assert.Contains(summary.Regions, region => !region.Test);
        Assert.Equal((summary.ReportId, summary.Summary, summary.Regions.Count), (await reports.GetAsync(room, Token)) is { } single ? (single.ReportId, single.Summary, single.Regions.Count) : default);
        Assert.Empty(await reports.ListReadyAsync(_world.Api.Clock.GetUtcNow().AddMinutes(1), Token));
        Assert.Null(await reports.GetAsync(Guid.NewGuid(), Token));
    }

    [Fact]
    public async Task Costing_more_than_the_target_per_extra_order_means_changing_the_campaign()
    {
        var (room, test) = await LiveAsync();
        _google.BidToTarget("search", "target_cpa", 2);
        await FinishAsync(room, test);

        var report = (await ViewAsync(room)).GetProperty("report");
        Assert.Equal(("worked", "change", 2m), (Text(report, "outcome"), Text(report, "nextStep"), report.GetProperty("targetCost").GetDecimal()));
        Assert.Equal(2m, Assert.Single(_report.Requests).GetProperty("targetCost").GetDecimal());
    }

    [Fact]
    public async Task A_conclusive_fall_stops_the_campaign()
    {
        var (room, test) = await LiveAsync();
        (_lift.Lift, _lift.Conclusive) = (-0.12, true);
        await FinishAsync(room, test);

        var report = (await ViewAsync(room)).GetProperty("report");
        Assert.Equal(("no_effect", "stop"), (Text(report, "outcome"), Text(report, "nextStep")));
        Assert.Equal(JsonValueKind.Null, report.GetProperty("costPerExtra").ValueKind);
    }

    [Fact]
    public async Task An_inconclusive_result_explains_itself_and_plans_the_next_test()
    {
        var (room, test) = await LiveAsync();
        _lift.Lift = 0.03;
        await FinishAsync(room, test);

        var report = (await ViewAsync(room)).GetProperty("report");
        Assert.Equal(("inconclusive", "retest"), (Text(report, "outcome"), Text(report, "nextStep")));
        Assert.Equal(("The range was too wide.", "Run a longer test."), (Text(report, "inconclusiveReason"), Text(report, "nextTestAdvice")));
        var next = report.GetProperty("nextTest");
        Assert.True(next.GetProperty("days").GetInt32() > 0);
        Assert.Contains("wide_range", next.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal(next.GetProperty("days").GetInt32(), Assert.Single(_report.Requests).GetProperty("nextTest").GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task Thin_data_is_reported_as_inconclusive()
    {
        var (room, test) = await LiveAsync();
        _lift.Status = "insufficient_data";
        await FinishAsync(room, test);

        var report = (await ViewAsync(room)).GetProperty("report");
        Assert.Equal(("inconclusive", "retest"), (Text(report, "outcome"), Text(report, "nextStep")));
        Assert.Contains("thin_data", report.GetProperty("nextTest").GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal("few_placebos", Text(Assert.Single(_report.Requests), "reason"));
    }

    [Fact]
    public async Task Plan_changes_an_early_end_and_account_changes_are_flagged()
    {
        var (room, test) = await LiveAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        using (var changed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, room, "proof-plan/changes",
                   new { version = Campaigns.Version(detail), reason = "Stock ran short in Pune", endDate = Campaigns.Iso(test.End!.Value.AddDays(-1)) }))
            Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync(Token));
        _google.Record("search", test.Start!.Value, 14, 1400, 35);
        _google.Record("shopping", test.Start.Value, 14, 1400, 35);
        _world.Api.GoogleApis.BigQuery.Selects["ads_campaign_daily"] = [["before", "2800"], ["during", "2700"]];
        await AdvanceToAsync(test.Start.Value.AddDays(9));
        await EndEarlyAsync(room);
        await CoverAsync(test.Start.Value.AddDays(9));
        await RunAsync();

        var report = (await ApiViewAsync(room)).GetProperty("report");
        var flags = report.GetProperty("flags").EnumerateArray().ToDictionary(flag => Text(flag, "code"), flag => Text(flag, "detail"));
        Assert.Equal(["changed_after_lock", "ended_early", "account_change"], flags.Keys);
        Assert.Contains("Stock ran short in Pune", flags["changed_after_lock"], StringComparison.Ordinal);
        Assert.Contains("after 9 of", flags["ended_early"], StringComparison.Ordinal);
        Assert.Contains("from about 100 to 300 a day", flags["account_change"], StringComparison.Ordinal);
        Assert.Equal(3, Assert.Single(_report.Requests).GetProperty("flags").GetArrayLength());

        var query = _world.Api.GoogleApis.BigQuery.Queries.Single(query => query.Sql.Contains("ads_campaign_daily", StringComparison.Ordinal));
        var parameters = query.Parameters.EnumerateArray().ToDictionary(parameter => Text(parameter, "name"));
        Assert.Equal("1111111111", parameters["customer"].GetProperty("parameterValue").GetProperty("value").GetString());
        Assert.Equal(_launch.Resources.Keys.Count(key => key.Contains("/campaigns/", StringComparison.Ordinal)),
            parameters["exclude"].GetProperty("parameterValue").GetProperty("arrayValues").GetArrayLength());
        Assert.Equal(Campaigns.Iso(test.Start.Value.AddDays(-28)), parameters["from"].GetProperty("parameterValue").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Writing_failures_retry_and_give_up_after_three_runs()
    {
        var (room, test) = await LiveAsync();
        _report.Fails = true;
        await FinishAsync(room, test);
        Assert.Equal("writing", (await ViewAsync(room)).GetProperty("state").GetString());
        await RunAsync();
        Assert.Empty(await AuditAsync("measurement.report_failed"));
        await RunAsync();
        await RunAsync();

        Assert.Equal(3, _report.Requests.Count);
        var view = await ViewAsync(room);
        Assert.Equal("failed", view.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("report").ValueKind);
        Assert.Single(await AuditAsync("measurement.report_failed"));
        var problem = Assert.Single(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId),
            row => row.Kind == "connection.problem" && row.Subject == $"impact:{room:N}");
        Assert.Equal(_world.Brand.Admin.UserId, problem.UserId);
        Assert.DoesNotContain(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "report.ready");
        await using (var scope = await ScopeAsync())
            Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().ImpactReports.SingleAsync(Token)).Attempts);
    }

    [Fact]
    public async Task A_failure_followed_by_success_still_writes_the_report()
    {
        var (room, test) = await LiveAsync();
        _report.Fails = true;
        await FinishAsync(room, test);
        _report.Fails = false;
        await RunAsync();

        Assert.Equal("ready", (await ViewAsync(room)).GetProperty("state").GetString());
        Assert.Empty(await AuditAsync("measurement.report_failed"));
    }

    [Fact]
    public async Task Ad_results_map_back_to_the_launched_assets()
    {
        var (room, test) = await LiveAsync();
        Dictionary<string, string> resources;
        await using (var scope = await ScopeAsync())
            resources = JsonSerializer.Deserialize<Dictionary<string, string>>(
                (await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.SingleAsync(row => row.RoomId == room, Token)).Resources)!;
        var ads = resources.Where(pair => pair.Key.StartsWith("ad:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(ads);
        foreach (var (ad, index) in ads.Select((ad, index) => (ad, index)))
            _google.AdMetrics[ad.Value] = (100 + index * 2, 500m + index, 10m + index);

        IReadOnlyList<AdResult> results;
        await using (var scope = await ScopeAsync())
            results = await scope.ServiceProvider.GetRequiredService<ICampaignChanges>().AdResultsAsync(room, test.Start!.Value, test.End!.Value, Token);

        Assert.Equal(ads.Select(ad => ad.Key).Order(), results.Select(result => result.Key).Order());
        foreach (var result in results)
        {
            var index = ads.FindIndex(ad => ad.Key == result.Key);
            Assert.Equal((100L + index * 2, 500m + index, 10m + index), (result.Clicks, result.Cost, result.Conversions));
            var kind = result.Key.Split(':')[1] switch
            {
                "search" => "search_ad",
                "shopping" => "shopping_title",
                "demand_gen" or "video" => "demand_gen_text",
                var other => other,
            };
            Assert.Equal(kind, result.Assets[0].Kind);
            Assert.Equal(result.Assets[0].Language, result.Language);
            Assert.Contains(result.Assets[0].Id.ToString("N"), result.Key, StringComparison.Ordinal);
            if (result.Key.StartsWith("ad:video:", StringComparison.Ordinal)) Assert.Equal("video", result.Assets[1].Kind);
        }

        var query = Assert.Single(_google.AdQueries);
        Assert.Contains($"BETWEEN '{Campaigns.Iso(test.Start.Value)}' AND '{Campaigns.Iso(test.End.Value)}'", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_stay_inside_their_workspace()
    {
        var (room, test) = await LiveAsync();
        var measuring = await ApiViewAsync(room);
        Assert.Equal(("measuring", "live"), (Text(measuring, "state"), Text(measuring, "stage")));
        Assert.Equal(JsonValueKind.Null, measuring.GetProperty("report").ValueKind);
        using var foreign = await _world.Marketer.GetAsync($"/api/brands/{Guid.NewGuid()}/campaigns/{room}/impact-report", Token);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var missing = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{Guid.NewGuid()}/impact-report", Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await FinishAsync(room, test);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().ImpactReports.ToListAsync(Token));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IImpactReports>().ListReadyAsync(DateTimeOffset.MinValue, Token));
    }

    [Fact]
    public async Task The_additive_migration_keeps_lift_readings_and_reports_start_after_it()
    {
        var (room, test) = await LiveAsync();
        _report.Fails = true;
        await FinishAsync(room, test);
        await using (var scope = await ScopeAsync())
        {
            var migrator = scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261016141609_LiftReadings", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
        }

        await using (var scope = await ScopeAsync())
            Assert.Single(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().LiftReadings.Where(row => !row.Early).ToListAsync(Token));
        _report.Fails = false;
        await RunAsync();
        Assert.Equal("ready", (await ViewAsync(room)).GetProperty("state").GetString());
    }

    private async Task FinishAsync(Guid room, CampaignTest test)
    {
        var end = test.End!.Value;
        var days = end.DayNumber - test.Start!.Value.DayNumber + 1;
        if (!_google.Days.ContainsKey("search"))
        {
            _google.Record("search", test.Start.Value, days, 1400, 35);
            _google.Record("shopping", test.Start.Value, days, 1400, 35);
        }

        await AdvanceToAsync(end.AddDays(1));
        await RunJobAsync("/jobs/campaigns/test-ends");
        await CoverAsync(end);
        await RunAsync();
    }

    private async Task<(Guid Room, CampaignTest Test)> LiveAsync()
    {
        var room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        Assert.Equal("live", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
        await using var testScope = await ScopeAsync();
        return (room, (await testScope.ServiceProvider.GetRequiredService<ICampaignTests>().GetAsync(room, Token))!);
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

    private Task<JsonElement> ApiViewAsync(Guid room) =>
        _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{room}/impact-report", Token);

    private async Task<JsonElement> ViewAsync(Guid room)
    {
        await using var scope = await ScopeAsync();
        var result = await ImpactEndpoints.GetAsync(room, scope.ServiceProvider.GetRequiredService<MeasurementDbContext>(),
            scope.ServiceProvider.GetRequiredService<ICampaignTests>(), Token);
        return JsonSerializer.SerializeToElement(Assert.IsType<Ok<ImpactView>>(result.Result).Value, JsonSerializerOptions.Web);
    }

    private async Task<string[]> AuditAsync(string action)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; SELECT Action FROM audit.Entries WHERE BrandId = @brand AND Action = @action";
        command.Parameters.AddWithValue("@brand", _world.BrandId);
        command.Parameters.AddWithValue("@action", action);
        var actions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) actions.Add(reader.GetString(0));
        return [.. actions];
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
