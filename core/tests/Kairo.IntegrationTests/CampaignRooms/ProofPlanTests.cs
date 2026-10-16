using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Opportunities;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class ProofPlanTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _world.Api.GoogleApis.BigQuery;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task The_proof_plan_follows_the_brief_using_city_history()
    {
        CityHistory();

        var roomId = await _world.OpenWithBriefAsync();

        var request = Assert.Single(_world.Proof.Requests);
        Assert.Equal(365, request.GetProperty("days").GetInt32());
        Assert.Equal(0.10, request.GetProperty("targetLift").GetDouble());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("holdoutIds").ValueKind);
        var regions = request.GetProperty("regions").EnumerateArray().ToDictionary(region => region.GetProperty("id").GetString()!);
        Assert.Equal(["mumbai", "nagpur", "nashik", "pune", "chennai"], regions.Keys);
        Assert.True(regions["mumbai"].GetProperty("inArea").GetBoolean());
        Assert.False(regions["chennai"].GetProperty("inArea").GetBoolean());
        Assert.Equal(365, regions["pune"].GetProperty("conversions").GetArrayLength());
        Assert.Equal(12, regions["mumbai"].GetProperty("searches").GetArrayLength());
        var asked = _world.Api.GoogleApis.Keywords.Calls.Where(call => call.Method == "history" && call.Body.GetProperty("geoTargetConstants")[0].GetString() == "geoTargetConstants/1007785");
        Assert.NotEmpty(asked);

        var result = await PlanAsync(roomId);
        Assert.False(result.GetProperty("updating").GetBoolean());
        Assert.True(result.GetProperty("canChooseHoldouts").GetBoolean());
        var plan = result.GetProperty("plan");
        Assert.Equal((1, 1, "city", "orders"), (plan.GetProperty("version").GetInt32(), plan.GetProperty("briefVersion").GetInt32(), plan.GetProperty("unit").GetString(), plan.GetProperty("kpi").GetString()));
        Assert.Equal(["Mumbai"], Names(plan, "testRegions").Take(1));
        Assert.Equal(["Nagpur", "Nashik"], Names(plan, "holdouts"));
        var mumbai = plan.GetProperty("testRegions")[0];
        Assert.Equal(("Maharashtra", 19.076), (mumbai.GetProperty("state").GetString(), mumbai.GetProperty("latitude").GetDouble()));
        var nagpur = plan.GetProperty("holdouts")[0];
        Assert.Equal(89.0, nagpur.GetProperty("matchScore").GetDouble());
        Assert.Equal(4, plan.GetProperty("candidates").GetArrayLength());
        var window = plan.GetProperty("window");
        var start = Today().AddDays(1);
        Assert.Equal(Iso(start), window.GetProperty("start").GetString());
        Assert.Equal(Iso(start.AddDays(13)), window.GetProperty("end").GetString());
        Assert.Equal(14, window.GetProperty("days").GetInt32());
        Assert.Equal(0.08, plan.GetProperty("minDetectableLift").GetDouble());
        Assert.Equal((1120.5m, "INR"), (plan.GetProperty("holdoutCost").GetProperty("amount").GetDecimal(), plan.GetProperty("holdoutCost").GetProperty("currency").GetString()));
        Assert.Empty(plan.GetProperty("warnings").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("lockedAt").ValueKind);

        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("in_progress", Campaigns.Status(detail));
        var run = detail.GetProperty("runs")[0];
        Assert.Equal(("proof_plan", "succeeded", "placebo-geo/1"), (run.GetProperty("kind").GetString(), run.GetProperty("status").GetString(), run.GetProperty("promptVersion").GetString()));
    }

    [Fact]
    public async Task Without_city_history_the_plan_tests_whole_states()
    {
        BigQuery.Selects["ads_region_daily"] =
        [
            .. Days(120).SelectMany(day => new[]
            {
                new[] { Iso(day), "21342", "Maharashtra", null, "5", "INR" },
                new[] { Iso(day), "21333", "Kerala", null, "3", "INR" },
                new[] { Iso(day), "21345", "Tamil Nadu", null, "4", "INR" },
            }),
        ];

        var roomId = await _world.OpenWithBriefAsync();

        var request = Assert.Single(_world.Proof.Requests);
        Assert.Contains("maharashtra", request.GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("id").GetString()));
        var plan = (await PlanAsync(roomId)).GetProperty("plan");
        Assert.Equal("state", plan.GetProperty("unit").GetString());
        Assert.Equal("state_fallback", plan.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Contains(BigQuery.Queries, query => query.Sql.Contains(".ads_city_daily`", StringComparison.Ordinal));
        Assert.Contains(BigQuery.Queries, query => query.Sql.Contains(".ads_region_daily`", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Warnings_from_the_test_design_reach_the_plan()
    {
        CityHistory();
        _world.Proof.Warnings = [new { code = "short_history", message = "There's less than 8 weeks of history." }];

        var roomId = await _world.OpenWithBriefAsync();

        var warning = Assert.Single((await PlanAsync(roomId)).GetProperty("plan").GetProperty("warnings").EnumerateArray());
        Assert.Equal(("short_history", "There's less than 8 weeks of history."), (warning.GetProperty("code").GetString(), warning.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task A_marketer_can_swap_holdouts_from_the_ranked_list()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var swapped = await ChooseAsync(_world.Marketer, roomId, ["pune", "nashik"], Campaigns.Version(detail));

        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);
        Assert.Equal("agent_working", Campaigns.Status(await swapped.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.True((await PlanAsync(roomId)).GetProperty("updating").GetBoolean());
        Assert.False((await PlanAsync(roomId)).GetProperty("canChooseHoldouts").GetBoolean());

        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(["pune", "nashik"], _world.Proof.Requests[^1].GetProperty("holdoutIds").EnumerateArray().Select(id => id.GetString()));
        var plan = (await PlanAsync(roomId)).GetProperty("plan");
        Assert.Equal(2, plan.GetProperty("version").GetInt32());
        Assert.True(plan.GetProperty("chosenByUser").GetBoolean());
        Assert.Equal(["Pune", "Nashik"], Names(plan, "holdouts"));
        Assert.Equal(["Mumbai", "Nagpur"], Names(plan, "testRegions"));
        var activity = (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries");
        Assert.Contains(activity.EnumerateArray(), entry => entry.GetProperty("action").GetString() == "proof_plan.holdouts_chosen"
            && entry.GetProperty("label").GetString() == "Chose the holdout regions");
    }

    [Fact]
    public async Task A_retried_swap_keeps_the_chosen_holdouts()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        (await ChooseAsync(_world.Marketer, roomId, ["pune"], Campaigns.Version(detail))).EnsureSuccessStatusCode();
        _world.Proof.Fails = true;
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await _world.RunStagesAsync());
        var failed = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        var run = failed.GetProperty("actions").GetProperty("retryRun").GetGuid();

        _world.Proof.Fails = false;
        (await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"stage-runs/{run}/retry", new { })).EnsureSuccessStatusCode();
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(["pune"], _world.Proof.Requests[^1].GetProperty("holdoutIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(["Pune"], Names((await PlanAsync(roomId)).GetProperty("plan"), "holdouts"));
    }

    [Fact]
    public async Task Holdout_choices_are_checked()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var version = Campaigns.Version(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId));

        using var unknown = await ChooseAsync(_world.Marketer, roomId, ["chennai"], version);
        using var everything = await ChooseAsync(_world.Marketer, roomId, ["mumbai", "pune", "nagpur", "nashik"], version);
        using var none = await ChooseAsync(_world.Marketer, roomId, [], version);
        using var stale = await ChooseAsync(_world.Marketer, roomId, ["pune"], version - 1);
        using var approver = await ChooseAsync(_world.Approver.Browser, roomId, ["pune"], version);
        using var missing = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{Guid.NewGuid()}/proof-plan", Token);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, everything.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Single(_world.Proof.Requests);
    }

    [Fact]
    public async Task Holdouts_stay_put_while_approval_is_pending()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Token);

        using var swapped = await ChooseAsync(_world.Marketer, roomId, ["pune"], Campaigns.Version(pending));

        Assert.Equal(HttpStatusCode.Conflict, swapped.StatusCode);
        Assert.Equal("Approval has been requested, so the test plan can't change.", await Campaigns.TitleAsync(swapped));
        Assert.False((await PlanAsync(roomId)).GetProperty("canChooseHoldouts").GetBoolean());
    }

    [Fact]
    public async Task Before_the_plan_is_ready_there_is_nothing_to_swap()
    {
        var roomId = await _world.AcceptAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        var result = await PlanAsync(roomId);
        using var swapped = await ChooseAsync(_world.Marketer, roomId, ["pune"], Campaigns.Version(detail));

        Assert.Equal(JsonValueKind.Null, result.GetProperty("plan").ValueKind);
        Assert.False(result.GetProperty("updating").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, swapped.StatusCode);
    }

    [Fact]
    public async Task Changing_the_briefs_dates_or_regions_redesigns_the_test_with_them()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var version = Campaigns.Version(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId));
        var start = Today().AddDays(3);

        using var edited = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new
        {
            version,
            briefVersion = 1,
            regions = new[] { "Maharashtra" },
            startDate = Iso(start),
            endDate = Iso(start.AddDays(20)),
        });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.False((await edited.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("editable").GetBoolean());
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("agent_working", Campaigns.Status(detail));
        Assert.Equal(("proof_plan", "queued"), (detail.GetProperty("runs")[0].GetProperty("kind").GetString(), detail.GetProperty("runs")[0].GetProperty("status").GetString()));

        Assert.Equal(1, await _world.RunStagesAsync());

        var request = _world.Proof.Requests[^1];
        Assert.Equal(21, request.GetProperty("maxDays").GetInt32());
        Assert.Equal(["mumbai", "nagpur", "nashik", "pune"], request.GetProperty("regions").EnumerateArray()
            .Where(region => region.GetProperty("inArea").GetBoolean())
            .Select(region => region.GetProperty("id").GetString()));
        var plan = (await PlanAsync(roomId)).GetProperty("plan");
        Assert.Equal((2, 2), (plan.GetProperty("version").GetInt32(), plan.GetProperty("briefVersion").GetInt32()));
        Assert.Equal(Iso(start), plan.GetProperty("window").GetProperty("start").GetString());
    }

    [Fact]
    public async Task New_dates_keep_the_holdouts_the_marketer_chose_but_new_regions_do_not()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        (await ChooseAsync(_world.Marketer, roomId, ["pune"], Campaigns.Version(detail))).EnsureSuccessStatusCode();
        Assert.Equal(1, await _world.RunStagesAsync());
        var version = Campaigns.Version(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId));
        var start = Today().AddDays(2);

        using var moved = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 1, startDate = Iso(start), endDate = Iso(start.AddDays(13)) });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(["pune"], _world.Proof.Requests[^1].GetProperty("holdoutIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(["Pune"], Names((await PlanAsync(roomId)).GetProperty("plan"), "holdouts"));

        var next = (await moved.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("campaignVersion").GetInt32() + 1;
        using var regions = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version = next, briefVersion = 2, regions = new[] { "Maharashtra" } });
        Assert.Equal(HttpStatusCode.OK, regions.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());

        Assert.Equal(JsonValueKind.Null, _world.Proof.Requests[^1].GetProperty("holdoutIds").ValueKind);
    }

    [Fact]
    public async Task Changes_that_leave_dates_and_regions_alone_keep_the_test()
    {
        CityHistory();
        var roomId = await _world.OpenWithBriefAsync();
        var version = Campaigns.Version(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId));

        using var edited = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 1, languages = new[] { "en", "hi" } });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("in_progress", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
        Assert.Equal(0, await _world.RunStagesAsync());
        Assert.Single(_world.Proof.Requests);
    }

    [Fact]
    public async Task An_applied_instruction_that_moves_the_regions_redesigns_the_test()
    {
        CityHistory();
        _world.Plan.Changes = _ => new { regions = new[] { "Maharashtra" }, reasons = new { regions = "Only Maharashtra." }, summary = "Keeps Maharashtra." };
        var roomId = await _world.OpenWithBriefAsync();
        var version = Campaigns.Version(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId));
        using var previewed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = "only Maharashtra" });
        var id = (await previewed.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();

        using var applied = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{id}/apply", new { version });

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(2, _world.Proof.Requests.Count);
        Assert.Equal(2, (await PlanAsync(roomId)).GetProperty("plan").GetProperty("briefVersion").GetInt32());
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private void CityHistory() => _world.LoadCityHistory();

    private DateOnly Today() => _world.Today();

    private IEnumerable<DateOnly> Days(int count)
    {
        var yesterday = Today().AddDays(-1);
        return Enumerable.Range(0, count).Select(offset => yesterday.AddDays(-offset));
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IEnumerable<string?> Names(JsonElement plan, string list) =>
        plan.GetProperty(list).EnumerateArray().Select(region => region.GetProperty("name").GetString());

    private Task<JsonElement> PlanAsync(Guid roomId) =>
        _world.Marketer.GetFromJsonAsync<JsonElement>($"{Campaigns.Base(_world.BrandId)}/{roomId}/proof-plan", Token);

    private Task<HttpResponseMessage> ChooseAsync(HttpClient browser, Guid roomId, string[] regionIds, int version) =>
        Campaigns.PostAsync(browser, _world.BrandId, roomId, "proof-plan/holdouts", new { regionIds, version });
}
