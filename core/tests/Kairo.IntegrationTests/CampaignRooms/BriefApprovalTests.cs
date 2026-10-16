using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Identity;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.CampaignRooms.Briefs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class BriefApprovalTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private Member _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _owner = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        await SettingsAsync(200000m);
    }

    [Fact]
    public async Task A_brief_within_the_limit_goes_to_the_brief_approvers()
    {
        var roomId = await _world.OpenWithBriefAsync();

        var pending = await RequestAsync(roomId);

        var approval = pending.GetProperty("pendingApproval");
        Assert.Equal("approvers", approval.GetProperty("route").GetString());
        Assert.Equal(JsonValueKind.Null, approval.GetProperty("routeReason").ValueKind);
        Assert.Equal([_world.Approver.UserId], Approvers(approval));
        var requested = await ActivityAsync(roomId, "campaign_room.approval_requested");
        var details = requested.GetProperty("details").GetProperty("details");
        Assert.Equal(("approvers", 1, 1), (details.GetProperty("route").GetString(), details.GetProperty("briefVersion").GetInt32(), details.GetProperty("proofPlanVersion").GetInt32()));

        using var approved = await ApproveAsync(_world.Approver.Browser, pending);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("creating", Campaigns.Stage(await approved.Content.ReadFromJsonAsync<JsonElement>(Token)));
    }

    [Fact]
    public async Task A_brief_over_the_limit_goes_only_to_the_budget_owners()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await EditAsync(roomId, new { budget = 250000 });

        var pending = await RequestAsync(roomId);

        var approval = pending.GetProperty("pendingApproval");
        Assert.Equal("budget_owner", approval.GetProperty("route").GetString());
        Assert.Contains("above the", approval.GetProperty("routeReason").GetString());
        Assert.Equal([_owner.UserId], Approvers(approval));
        var asApprover = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(JsonValueKind.Null, asApprover.GetProperty("actions").GetProperty("approve").ValueKind);

        using var refused = await ApproveAsync(_world.Approver.Browser, pending);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Only a budget owner can approve this brief, because its budget is above the limit.", await Campaigns.TitleAsync(refused));

        using var approved = await ApproveAsync(_owner.Browser, pending);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("creating", Campaigns.Stage(await approved.Content.ReadFromJsonAsync<JsonElement>(Token)));
    }

    [Fact]
    public async Task An_over_limit_brief_cant_be_sent_for_approval_without_a_budget_owner()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await EditAsync(roomId, new { budget = 250000 });
        await SqlAsync("DELETE FROM [identity].ApprovalDuties WHERE BrandId = @brand AND Duty = N'BudgetOwner';");

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("actions").GetProperty("requestApproval").ValueKind);
        Assert.Equal(BriefGate.NoBudgetOwner, detail.GetProperty("actions").GetProperty("requestApprovalBlocker").GetString());

        using var refused = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(BriefGate.NoBudgetOwner, await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task When_the_limit_drops_after_the_request_only_a_budget_owner_can_approve()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var pending = await RequestAsync(roomId);

        await SettingsAsync(1000m);

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("budget_owner", detail.GetProperty("pendingApproval").GetProperty("route").GetString());
        using var refused = await ApproveAsync(_world.Approver.Browser, pending);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("The budget limit changed after approval was requested, so only a budget owner can approve this brief now.", await Campaigns.TitleAsync(refused));
        var asApprover = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(["brief_draft"], asApprover.GetProperty("actions").GetProperty("sendBackTo").EnumerateArray().Select(stage => stage.GetString()));

        using var approved = await ApproveAsync(_owner.Browser, pending);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task A_brief_with_blocking_problems_cant_be_sent_for_approval()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await SqlAsync("UPDATE campaign_rooms.Briefs SET Content = JSON_MODIFY(Content, '$.StartDate', '2020-01-01') WHERE BrandId = @brand;");

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("actions").GetProperty("requestApproval").ValueKind);
        using var refused = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("The start date has passed.", await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task Approval_waits_for_the_test_plan()
    {
        var roomId = await _world.AcceptAsync();
        Assert.Equal(1, await _world.RunStagesAsync());
        var designing = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var early = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(designing) });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(1, await _world.RunStagesAsync());
        await SqlAsync("DELETE FROM campaign_rooms.ProofPlans WHERE BrandId = @brand;");

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var refused = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The brief needs a test plan before approval. Try designing the test again.", await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task Approval_needs_a_test_plan_for_the_current_regions_and_dates()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var brief = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);
        var end = DateOnly.Parse(brief.GetProperty("brief").GetProperty("endDate").GetString()!);
        _world.Proof.Fails = true;
        await EditAsync(roomId, new { endDate = Campaigns.Iso(end.AddDays(-1)) });
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await _world.RunStagesAsync());

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var refused = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The test plan was designed for an earlier version of the brief. Try designing the test again.", await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task A_non_scope_edit_keeps_the_test_plan_current()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await EditAsync(roomId, new { languages = new[] { "en", "hi" } });

        var pending = await RequestAsync(roomId);

        var details = (await ActivityAsync(roomId, "campaign_room.approval_requested")).GetProperty("details").GetProperty("details");
        Assert.Equal((2, 1), (details.GetProperty("briefVersion").GetInt32(), details.GetProperty("proofPlanVersion").GetInt32()));
        using var approved = await ApproveAsync(_world.Approver.Browser, pending);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task The_brief_and_test_plan_cant_change_while_approval_is_pending()
    {
        _world.LoadCityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var pending = await RequestAsync(roomId);

        using var edit = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version = Campaigns.Version(pending), briefVersion = 1, budget = 5000 });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        using var swap = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "proof-plan/holdouts", new { regionIds = new[] { "pune" }, version = Campaigns.Version(pending) });
        Assert.Equal(HttpStatusCode.Conflict, swap.StatusCode);

        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "brief_draft", comment = "Lower the budget.", version = Campaigns.Version(pending) });
        var returned = await back.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var later = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version = Campaigns.Version(returned), briefVersion = 1, budget = 5000 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Fact]
    public async Task Approving_a_thin_data_test_plan_records_the_weaker_test()
    {
        _world.Proof.Warnings = [new { code = "short_history", message = "There's less than 8 weeks of history." }];
        var roomId = await _world.OpenWithBriefAsync();

        await _world.ApproveBriefAsync(roomId);

        var plan = (await Campaigns.PlanAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("plan");
        Assert.True(plan.GetProperty("approvedWithWarnings").GetBoolean());
        var approved = await ActivityAsync(roomId, "campaign_room.approved");
        Assert.True(approved.GetProperty("details").GetProperty("details").GetProperty("approvedWithWarnings").GetBoolean());
    }

    [Fact]
    public async Task Launch_approval_locks_the_test_plan()
    {
        _world.LoadCityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var before = await Campaigns.PlanAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("plan").GetProperty("lockedAt").ValueKind);
        Assert.False(before.GetProperty("canChangeAfterLock").GetBoolean());

        Assert.Equal("launching", Campaigns.Stage(await _world.LaunchAsync(roomId)));

        var after = await Campaigns.PlanAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(_world.Api.Clock.GetUtcNow(), after.GetProperty("plan").GetProperty("lockedAt").GetDateTimeOffset());
        Assert.False(after.GetProperty("canChooseHoldouts").GetBoolean());
        Assert.True(after.GetProperty("canChangeAfterLock").GetBoolean());
        var locked = await ActivityAsync(roomId, "proof_plan.locked");
        Assert.Equal("Locked the test plan at launch", locked.GetProperty("label").GetString());
        var current = await CurrentAsync(roomId);
        Assert.NotNull(current!.LockedAt);
        Assert.False(current.ChangedAfterLock);
    }

    [Fact]
    public async Task A_test_plan_change_after_launch_needs_a_reason_and_is_flagged()
    {
        _world.LoadCityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var draft = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var early = await ChangeAsync(roomId, new { version = Campaigns.Version(draft), reason = "Pune had a strike.", holdoutRegionIds = new[] { "nashik" } });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("The test plan locks at launch. Until then, change the brief or choose holdout regions instead.", await Campaigns.TitleAsync(early));

        var launched = await _world.LaunchAsync(roomId);
        var version = Campaigns.Version(launched);
        var locked = (await Campaigns.PlanAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("plan");
        var holdouts = locked.GetProperty("holdouts").EnumerateArray().Select(region => region.GetProperty("id").GetString()!).ToArray();
        var end = DateOnly.Parse(locked.GetProperty("window").GetProperty("end").GetString()!);

        using var noReason = await ChangeAsync(roomId, new { version, reason = " ", holdoutRegionIds = new[] { "nashik" } });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using var nothing = await ChangeAsync(roomId, new { version, reason = "Checking.", holdoutRegionIds = holdouts });
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
        using var unknown = await ChangeAsync(roomId, new { version, reason = "Checking.", holdoutRegionIds = new[] { "atlantis" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        await SqlAsync("UPDATE campaign_rooms.ProofPlans SET Content = JSON_MODIFY(Content, '$.startDate', '2020-01-01') WHERE BrandId = @brand;");
        using var past = await ChangeAsync(roomId, new { version, reason = "End it early.", endDate = Campaigns.Iso(_world.Today().AddDays(-1)) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Contains("not on a day that has passed", await past.Content.ReadAsStringAsync(Token));

        using var changed = await ChangeAsync(roomId, new { version, reason = "Pune had a strike.", holdoutRegionIds = new[] { "nashik" }, endDate = Campaigns.Iso(end.AddDays(-2)) });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var plan = (await changed.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("plan");
        Assert.Equal(locked.GetProperty("version").GetInt32() + 1, plan.GetProperty("version").GetInt32());
        Assert.True(plan.GetProperty("changedAfterLock").GetBoolean());
        Assert.Equal("Pune had a strike.", plan.GetProperty("changeReason").GetString());
        Assert.Equal(locked.GetProperty("lockedAt").GetDateTimeOffset(), plan.GetProperty("lockedAt").GetDateTimeOffset());
        Assert.Equal(["nashik"], plan.GetProperty("holdouts").EnumerateArray().Select(region => region.GetProperty("id").GetString()));
        Assert.DoesNotContain(plan.GetProperty("testRegions").EnumerateArray(), region => region.GetProperty("id").GetString() == "nashik");
        Assert.Equal(Campaigns.Iso(end.AddDays(-2)), plan.GetProperty("window").GetProperty("end").GetString());
        Assert.Contains(plan.GetProperty("warnings").EnumerateArray(), warning => warning.GetProperty("code").GetString() == "changed_after_lock");

        var audited = await ActivityAsync(roomId, "proof_plan.changed_after_lock");
        Assert.Equal("Pune had a strike.", audited.GetProperty("details").GetProperty("reason").GetString());
        var current = await CurrentAsync(roomId);
        Assert.True(current!.ChangedAfterLock);
        Assert.Equal(["Pune had a strike."], current.Changes.Select(change => change.Reason));
        Assert.Equal(["nashik"], current.Holdouts.Select(region => region.Id));
    }

    [Theory]
    [InlineData(null, "INR", 5000, "INR", "approvers")]
    [InlineData(200000, "INR", 200000, "INR", "approvers")]
    [InlineData(200000, "INR", 200001, "INR", "budget_owner")]
    [InlineData(200000, "INR", 100, "USD", "budget_owner")]
    [InlineData(200000, "", 100, "USD", "approvers")]
    [InlineData(200000, "INR", 0, "INR", "approvers")]
    public void The_budget_limit_decides_the_route(int? limit, string limitCurrency, int amount, string currency, string route)
    {
        var budget = new BriefBudget { Amount = amount, Currency = currency };

        Assert.Equal(route, BriefRoute.Of(budget, new BudgetCap(limit, limitCurrency)).Route);
    }

    [Fact]
    public void Only_regions_and_dates_make_a_test_plan_stale()
    {
        var designed = new BriefContent { Regions = ["Mumbai", "Pune"], StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 14), Languages = ["en"] };
        var reworded = designed.Copy();
        reworded.Languages = ["en", "hi"];
        reworded.Regions = ["pune", "Mumbai "];
        var moved = designed.Copy();
        moved.EndDate = new DateOnly(2026, 11, 20);
        var widened = designed.Copy();
        widened.Regions = ["Mumbai", "Pune", "Nagpur"];

        Assert.True(BriefGate.SameScope(designed, reworded));
        Assert.False(BriefGate.SameScope(designed, moved));
        Assert.False(BriefGate.SameScope(designed, widened));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static Guid[] Approvers(JsonElement approval) =>
        [.. approval.GetProperty("approvers").EnumerateArray().Select(person => person.GetProperty("id").GetGuid())];

    private async Task SettingsAsync(decimal limit)
    {
        using var response = await _world.Admin.PutAsJsonAsync($"/api/brands/{_world.BrandId}/approval-settings", new
        {
            briefApprovers = new[] { _world.Approver.UserId },
            assetApprovers = new[] { _world.Approver.UserId },
            launchApprovers = new[] { _world.Approver.UserId },
            budgetOwners = new[] { _owner.UserId },
            budgetMoveApprovers = new[] { _world.Approver.UserId },
            targetChangeApprovers = new[] { _world.Approver.UserId },
            googleSuggestionApprovers = new[] { _world.Approver.UserId },
            budgetLimit = limit,
        }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task EditAsync(Guid roomId, object changes)
    {
        var brief = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);
        var body = JsonSerializer.SerializeToNode(changes, JsonSerializerOptions.Web)!.AsObject();
        body["version"] = brief.GetProperty("campaignVersion").GetInt32();
        body["briefVersion"] = brief.GetProperty("brief").GetProperty("version").GetInt32();
        using var response = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> RequestAsync(Guid roomId)
    {
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("brief", detail.GetProperty("actions").GetProperty("requestApproval").GetString());
        using var response = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private Task<HttpResponseMessage> ApproveAsync(HttpClient browser, JsonElement pending) =>
        Campaigns.PostAsync(browser, _world.BrandId, pending.GetProperty("id").GetGuid(), "approve", new { gate = "brief", version = Campaigns.Version(pending) });

    private Task<HttpResponseMessage> ChangeAsync(Guid roomId, object body) =>
        Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "proof-plan/changes", body);

    private async Task<JsonElement> ActivityAsync(Guid roomId, string action) =>
        (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray()
            .First(entry => entry.GetProperty("action").GetString() == action);

    private async Task<CurrentProofPlan?> CurrentAsync(Guid roomId)
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Kairo.SharedKernel.Persistence.KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IProofPlans>().GetCurrentAsync(roomId, Token);
    }

    private async Task SqlAsync(string sql)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; {sql}";
        command.Parameters.AddWithValue("@brand", _world.BrandId);
        await command.ExecuteNonQueryAsync(Token);
    }
}
