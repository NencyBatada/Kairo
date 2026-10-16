using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Launch;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Care.Data;
using Kairo.Modules.Care.Recommendations;
using Kairo.Modules.Launch;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Care;

public sealed class RecommendationTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private FakeCareGoogle _google = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _google = new FakeCareGoogle(_launch);
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Ending_a_test_early_moves_the_campaign_to_results_and_flags_it()
    {
        var (room, _) = await LiveAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        var adminOnly = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "admin");
        using (var admin = await Campaigns.PostAsync(adminOnly.Browser, _world.BrandId, room, "end-test", new { version = Campaigns.Version(detail) }))
            Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        using (var ended = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, room, "end-test", new { version = Campaigns.Version(detail) }))
        {
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
            Assert.Equal("results_ready", Campaigns.Stage(await ended.Content.ReadFromJsonAsync<JsonElement>(Token)));
        }

        var again = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        using (var conflict = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, room, "end-test", new { version = Campaigns.Version(again) }))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var list = await ListAsync(room);
        Assert.True(list.GetProperty("test").GetProperty("endedEarly").GetBoolean());
        Assert.False(list.GetProperty("test").GetProperty("running").GetBoolean());
        var activity = await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, room);
        Assert.Contains(activity.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("action").GetString() == "campaign_room.test_ended");
    }

    [Fact]
    public async Task The_hourly_job_ends_tests_only_after_their_window_closes()
    {
        var (room, window) = await LiveAsync();
        await AdvanceToAsync(window.End);
        await RunJobAsync("/jobs/campaigns/test-ends");
        Assert.Equal("live", (await TestAsync(room)).Stage);
        await AdvanceToAsync(window.End.AddDays(1));
        await RunJobAsync("/jobs/campaigns/test-ends");
        var ended = await TestAsync(room);
        Assert.Equal(("results_ready", false), (ended.Stage, ended.EndedEarly));
    }

    [Fact]
    public async Task A_finished_test_drafts_one_budget_move_and_asks_the_assigned_approver()
    {
        var (room, window) = await LiveAsync();
        await AdvanceToAsync(window.Start.AddDays(6));
        _google.Record("search", window.Start, 6, 400, 40);
        _google.Record("shopping", window.Start, 6, 900, 30);
        await EndEarlyAsync(room);
        await RunJobAsync("/jobs/care/recommendations");
        await RunJobAsync("/jobs/care/recommendations");

        var recommendations = Items(await ListAsync(room));
        var move = Assert.Single(recommendations);
        Assert.Equal(("kairo", "budget_move", "proposed", "budget_moves"), (Text(move, "source"), Text(move, "type"), Text(move, "state"), Text(move, "duty")));
        Assert.Contains("from Shopping to Search", Text(move, "title"), StringComparison.Ordinal);
        Assert.Contains("more orders for the same total spend", Text(move, "expectedEffect"), StringComparison.Ordinal);
        Assert.False(move.GetProperty("canApprove").GetBoolean());
        var budgets = move.GetProperty("change").GetProperty("budgets").EnumerateArray().ToDictionary(item => Text(item, "campaignType"), item => item.GetProperty("total").GetDecimal());
        Assert.Equal(_google.TotalBudget("shopping") + _google.TotalBudget("search"), budgets.Values.Sum());
        Assert.True(Items(await ListAsync(room, _world.Approver.Browser)).Single().GetProperty("canApprove").GetBoolean());

        var notifications = await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId);
        var approval = Assert.Single(notifications, row => row.Kind == "approval.needed" && row.Subject!.StartsWith("recommendation:", StringComparison.Ordinal));
        Assert.Equal(_world.Approver.UserId, approval.UserId);
        Assert.Contains(notifications, row => row.Kind == "report.ready");
        Assert.Empty(_google.Writes());
    }

    [Fact]
    public async Task Approving_a_budget_move_validates_then_sets_both_budgets_once()
    {
        var room = await DraftedAsync();
        var move = Items(await ListAsync(room)).Single();
        var (search, shopping) = (_google.TotalBudget("search"), _google.TotalBudget("shopping"));
        using (var marketer = await ApproveAsync(_world.Marketer, move))
            Assert.Equal(HttpStatusCode.Forbidden, marketer.StatusCode);
        using (var approved = await ApproveAsync(_world.Approver.Browser, move))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal("applied", Text(await approved.Content.ReadFromJsonAsync<JsonElement>(Token), "state"));
        }

        var amount = move.GetProperty("change").GetProperty("amount").GetDecimal();
        Assert.Equal((search + amount, shopping - amount), (_google.TotalBudget("search"), _google.TotalBudget("shopping")));
        Assert.Equal(2, _google.Mutations.Count);
        Assert.True(_google.Mutations[0].GetProperty("validateOnly").GetBoolean());
        Assert.Equal(2, _google.Writes().Single().GetProperty("mutateOperations").GetArrayLength());
        using (var twice = await ApproveAsync(_world.Approver.Browser, move))
            Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Single(_google.Writes());
        Assert.Contains(await AuditAsync("care.recommendation_applied"), entry => Text(entry, "targetId") == Text(move, "id"));
        Assert.NotNull(Assert.Single(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Subject == $"recommendation:{Guid.Parse(Text(move, "id")):N}").ResolvedAt);
    }

    [Fact]
    public async Task A_campaign_changed_after_drafting_expires_the_recommendation_without_writing()
    {
        var room = await DraftedAsync();
        var move = Items(await ListAsync(room)).Single();
        _google.Budget("search")["totalAmountMicros"] = "999000000";
        using var response = await ApproveAsync(_world.Approver.Browser, move);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed after Kairo made this recommendation", await Campaigns.TitleAsync(response), StringComparison.Ordinal);
        Assert.Equal("expired", Text(Items(await ListAsync(room)).Single(), "state"));
        Assert.Empty(_google.Mutations);
        Assert.NotEmpty(await AuditAsync("care.recommendation_expired"));
    }

    [Fact]
    public async Task A_rejected_change_returns_the_recommendation_to_proposed_with_the_problem()
    {
        var room = await DraftedAsync();
        var move = Items(await ListAsync(room)).Single();
        _google.FailWrites = true;
        using (var failed = await ApproveAsync(_world.Approver.Browser, move))
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var after = Items(await ListAsync(room)).Single();
        Assert.Equal("proposed", Text(after, "state"));
        Assert.Contains("Google rejected this change", Text(after, "problem"), StringComparison.Ordinal);
        _google.FailWrites = false;
        using var retried = await ApproveAsync(_world.Approver.Browser, after);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    }

    [Fact]
    public async Task Thin_test_data_drafts_nothing_and_is_not_drafted_again()
    {
        var (room, window) = await LiveAsync();
        await AdvanceToAsync(window.Start.AddDays(6));
        _google.Record("search", window.Start, 6, 1000, 10);
        _google.Record("shopping", window.Start, 6, 2000, 8);
        await EndEarlyAsync(room);
        await RunJobAsync("/jobs/care/recommendations");
        Assert.Empty(Items(await ListAsync(room)));
        _google.Record("search", window.Start, 6, 1000, 100);
        _google.Record("shopping", window.Start, 6, 2000, 80);
        await RunJobAsync("/jobs/care/recommendations");
        Assert.Empty(Items(await ListAsync(room)));
        Assert.DoesNotContain(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "report.ready");
    }

    [Fact]
    public async Task A_target_far_from_what_the_test_achieved_is_recommended_and_applied_to_the_bid_target()
    {
        var (room, window) = await LiveAsync();
        _google.BidToTarget("search", "target_cpa", 10);
        await AdvanceToAsync(window.Start.AddDays(6));
        _google.Record("search", window.Start, 6, 1200, 60);
        _google.Record("shopping", window.Start, 6, 1300, 60);
        await EndEarlyAsync(room);
        await RunJobAsync("/jobs/care/recommendations");

        var target = Assert.Single(Items(await ListAsync(room)));
        Assert.Equal(("target_change", "target_changes"), (Text(target, "type"), Text(target, "duty")));
        Assert.Equal(20m, target.GetProperty("change").GetProperty("target").GetDecimal());
        using var approved = await ApproveAsync(_world.Approver.Browser, target);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var update = _google.Writes().Single().GetProperty("mutateOperations")[0].GetProperty("campaignOperation");
        Assert.Equal("target_cpa.target_cpa_micros", update.GetProperty("updateMask").GetString());
        Assert.Equal("20000000", update.GetProperty("update").GetProperty("targetCpa").GetProperty("targetCpaMicros").GetString());
    }

    [Fact]
    public async Task Only_assigned_approvers_approve_and_marketers_can_dismiss_with_a_reason()
    {
        var room = await DraftedAsync();
        var other = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        var move = Items(await ListAsync(room)).Single();
        using (var forbidden = await ApproveAsync(other.Browser, move))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var tooLong = await PostAsync(_world.Marketer, move, "dismiss", new { version = move.GetProperty("version").GetInt32(), reason = new string('x', 501) }))
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using (var dismissed = await PostAsync(_world.Marketer, move, "dismiss", new { version = move.GetProperty("version").GetInt32(), reason = "Holding budget for the sale" }))
        {
            Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
            var view = await dismissed.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.Equal(("dismissed", "Holding budget for the sale"), (Text(view, "state"), Text(view, "dismissReason")));
        }

        Assert.NotEmpty(await AuditAsync("care.recommendation_dismissed"));
        Assert.Empty(_google.Mutations);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<CareDbContext>().Recommendations.ToListAsync(Token));
    }

    [Fact]
    public async Task Google_suggestions_that_could_reach_holdouts_stay_hidden_while_the_test_runs()
    {
        var (room, window) = await LiveAsync();
        _google.Suggestions.Add(new CareSuggestion("11", "KEYWORD", "search"));
        _google.Suggestions.Add(new CareSuggestion("12", "DISPLAY_EXPANSION_OPT_IN", "search"));
        _google.Suggestions.Add(new CareSuggestion("13", "NEW_GEO_EXPANSION_TYPE", "shopping"));
        await RunJobAsync("/jobs/care/recommendations");

        var during = Items(await ListAsync(room));
        var keyword = Assert.Single(during);
        Assert.Equal(("google", "KEYWORD", "google_suggestions"), (Text(keyword, "source"), Text(keyword, "type"), Text(keyword, "duty")));
        Assert.Contains("+5 conversions", Text(keyword, "expectedEffect"), StringComparison.Ordinal);
        Assert.Single(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Kind == "approval.needed" && row.Subject!.StartsWith("recommendation:", StringComparison.Ordinal));

        using (var approved = await ApproveAsync(_world.Approver.Browser, keyword))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(["customers/1111111111/recommendations/11"], _google.Applied);

        await AdvanceToAsync(window.Start.AddDays(2));
        await EndEarlyAsync(room);
        _google.Suggestions.RemoveAll(suggestion => suggestion.Id == "13");
        await RunJobAsync("/jobs/care/recommendations");
        var after = Items(await ListAsync(room));
        Assert.Equal(["DISPLAY_EXPANSION_OPT_IN", "KEYWORD", "NEW_GEO_EXPANSION_TYPE"], after.Select(item => Text(item, "type")).Order(StringComparer.Ordinal));
        Assert.Equal("expired", Text(after.Single(item => Text(item, "type") == "NEW_GEO_EXPANSION_TYPE"), "state"));
        Assert.Equal("applied", Text(after.Single(item => Text(item, "type") == "KEYWORD"), "state"));
    }

    [Fact]
    public async Task Google_suggestions_stop_refreshing_and_expire_once_the_campaigns_have_ended()
    {
        var (room, _) = await LiveAsync();
        var end = DateOnly.Parse(Text((await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, room)).GetProperty("brief"), "endDate"), CultureInfo.InvariantCulture);
        _google.Suggestions.Add(new CareSuggestion("31", "KEYWORD", "search"));
        await AdvanceToAsync(end);
        await RunJobAsync("/jobs/care/recommendations");
        Assert.Equal(RecommendationStates.Proposed, (await StoredAsync(room)).Single().State);
        var reads = _google.SuggestionReads;

        await AdvanceToAsync(end.AddDays(1));
        await RunJobAsync("/jobs/care/recommendations");
        await RunJobAsync("/jobs/care/recommendations");
        var expired = (await StoredAsync(room)).Single();
        Assert.Equal((RecommendationStates.Expired, "The campaign has ended."), (expired.State, expired.Problem));
        Assert.Equal(reads, _google.SuggestionReads);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        var trail = await scope.ServiceProvider.GetRequiredService<IAuditTrail>().ForTargetAsync("recommendation", expired.Id.ToString(), null, 10, Token);
        Assert.Single(trail.Entries, entry => entry.Action == "care.recommendation_expired");
    }

    [Fact]
    public async Task A_Google_budget_increase_needs_a_budget_owner()
    {
        var (room, _) = await LiveAsync();
        var owner = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        using (var settings = await _world.Admin.PutAsJsonAsync($"/api/brands/{_world.BrandId}/approval-settings", new
        {
            briefApprovers = new[] { _world.Approver.UserId }, assetApprovers = new[] { _world.Approver.UserId }, launchApprovers = new[] { _world.Approver.UserId },
            budgetOwners = new[] { owner.UserId }, budgetMoveApprovers = new[] { _world.Approver.UserId }, targetChangeApprovers = new[] { _world.Approver.UserId },
            googleSuggestionApprovers = new[] { _world.Approver.UserId, owner.UserId }, budgetLimit = 200000m,
        }, Token))
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        _google.Suggestions.Add(new CareSuggestion("21", "CAMPAIGN_BUDGET", "search"));
        await RunJobAsync("/jobs/care/recommendations");

        var budget = Items(await ListAsync(room)).Single();
        Assert.Equal(owner.UserId, Assert.Single(await NotificationRows.NotificationsAsync(_world.Database, _world.BrandId), row => row.Subject == $"recommendation:{Guid.Parse(Text(budget, "id")):N}").UserId);
        using (var approver = await ApproveAsync(_world.Approver.Browser, budget))
            Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        using var approved = await ApproveAsync(owner.Browser, budget);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(["customers/1111111111/recommendations/21"], _google.Applied);
    }

    [Fact]
    public async Task Google_read_failures_skip_the_campaign_until_the_next_run()
    {
        var (room, window) = await LiveAsync();
        await AdvanceToAsync(window.Start.AddDays(6));
        _google.Record("search", window.Start, 6, 400, 40);
        _google.Record("shopping", window.Start, 6, 900, 30);
        await EndEarlyAsync(room);
        _google.TransientReads = 1;
        await RunJobAsync("/jobs/care/recommendations");
        Assert.Empty(Items(await ListAsync(room)));
        await RunJobAsync("/jobs/care/recommendations");
        Assert.Single(Items(await ListAsync(room)));
    }

    private async Task<Guid> DraftedAsync()
    {
        var (room, window) = await LiveAsync();
        await AdvanceToAsync(window.Start.AddDays(6));
        _google.Record("search", window.Start, 6, 400, 40);
        _google.Record("shopping", window.Start, 6, 900, 30);
        await EndEarlyAsync(room);
        await RunJobAsync("/jobs/care/recommendations");
        return room;
    }

    private async Task<(Guid Room, (DateOnly Start, DateOnly End) Window)> LiveAsync()
    {
        var room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        Assert.Equal("live", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
        var test = (await ListAsync(room)).GetProperty("test");
        return (room, (DateOnly.Parse(Text(test, "start"), CultureInfo.InvariantCulture), DateOnly.Parse(Text(test, "end"), CultureInfo.InvariantCulture)));
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

    private async Task<List<Recommendation>> StoredAsync(Guid room)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return await scope.ServiceProvider.GetRequiredService<CareDbContext>().Recommendations.AsNoTracking().Where(row => row.RoomId == room).ToListAsync(Token);
    }

    private async Task<CampaignTest> TestAsync(Guid room)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return (await scope.ServiceProvider.GetRequiredService<ICampaignTests>().GetAsync(room, Token))!;
    }

    private async Task RunJobAsync(string path)
    {
        using var client = _world.Worker.CreateClient();
        using var response = await client.PostAsync(path, null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> ListAsync(Guid room, HttpClient? browser = null) =>
        await (browser ?? _world.Marketer).GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{room}/recommendations", Token);

    private Task<HttpResponseMessage> ApproveAsync(HttpClient browser, JsonElement recommendation) =>
        PostAsync(browser, recommendation, "approve", new { version = recommendation.GetProperty("version").GetInt32() });

    private Task<HttpResponseMessage> PostAsync(HttpClient browser, JsonElement recommendation, string action, object body) =>
        browser.PostAsJsonAsync($"/api/brands/{_world.BrandId}/recommendations/{Text(recommendation, "id")}/{action}", body, Token);

    private async Task<JsonElement[]> AuditAsync(string action) =>
        [.. (await _world.Admin.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/audit-log?action={action}", Token)).GetProperty("entries").EnumerateArray()];

    private static JsonElement[] Items(JsonElement list) => [.. list.GetProperty("recommendations").EnumerateArray()];

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
