using System.Net;
using System.Diagnostics;
using System.Collections.Concurrent;
using Kairo.SharedKernel.Observability;
using Kairo.IntegrationTests.Notifications;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class StageRunTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Generation_limit_failures_keep_a_stable_code_and_never_retry_automatically()
    {
        var room = await _world.AcceptAsync();
        _world.Plan.ErrorCode = "generation_limit_exceeded";
        Assert.Equal(1, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, await _world.RunStagesAsync());
        Assert.Single(_world.Plan.Requests);
        var run = Assert.Single((await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)).GetProperty("runs").EnumerateArray());
        Assert.Equal("failed", run.GetProperty("status").GetString());
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'kairo.all_brands', @value=1; SELECT ErrorCode FROM campaign_rooms.StageRuns WHERE RoomId=@room;";
        command.Parameters.AddWithValue("@room", room);
        Assert.Equal("generation_limit_exceeded", await command.ExecuteScalarAsync(Token));
    }

    [Fact]
    public async Task A_persisted_run_keeps_the_request_trace_when_a_later_worker_claims_it()
    {
        var stages = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KairoTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stages.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("accept").SetIdFormat(ActivityIdFormat.W3C).Start();
        _world.Marketer.DefaultRequestHeaders.Add("traceparent", parent.Id);
        Guid room;
        try { room = await _world.AcceptAsync(); }
        finally { _world.Marketer.DefaultRequestHeaders.Remove("traceparent"); }
        parent.Stop();
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT TOP (1) TraceParent FROM campaign_rooms.StageRuns WHERE RoomId = @room ORDER BY QueuedAt;
            """;
        command.Parameters.AddWithValue("@room", room);
        var stored = Assert.IsType<string>(await command.ExecuteScalarAsync(Token));
        Assert.True(ActivityContext.TryParse(stored, null, out var context));
        Assert.Equal(parent.TraceId, context.TraceId);
        Assert.Equal(1, await _world.RunStagesAsync());
        var stage = Assert.Single(stages, activity => activity.GetTagItem("kairo.campaign_id")?.ToString() == room.ToString());
        Assert.Equal(context.TraceId, stage.TraceId);
        Assert.Equal(context.SpanId, stage.ParentSpanId);
    }

    [Fact]
    public async Task Accepting_queues_the_Plan_agent_and_its_brief_lands_in_the_room()
    {
        var roomId = await _world.AcceptAsync();

        var queued = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(("brief_draft", "agent_working"), (Campaigns.Stage(queued), Campaigns.Status(queued)));
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("brief").ValueKind);
        var tracker = queued.GetProperty("tracker");
        Assert.Equal(("agent", "Plan agent"), (tracker.GetProperty("ownerKind").GetString(), tracker.GetProperty("owner").GetString()));

        Assert.Equal(1, await _world.RunStagesAsync());

        var briefed = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("agent_working", Campaigns.Status(briefed));
        Assert.Equal(["proof_plan", "brief"], briefed.GetProperty("runs").EnumerateArray().Select(row => row.GetProperty("kind").GetString()));

        Assert.Equal(1, await _world.RunStagesAsync());

        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("in_progress", Campaigns.Status(drafted));
        var brief = drafted.GetProperty("brief");
        Assert.Equal((1, "drafted", "Monsoon runners"), (brief.GetProperty("version").GetInt32(), brief.GetProperty("origin").GetString(), brief.GetProperty("title").GetString()));
        Assert.Equal(("gemini-test", "plan-brief/1"), (brief.GetProperty("model").GetString(), brief.GetProperty("promptVersion").GetString()));
        Assert.Single(brief.GetProperty("productIds").EnumerateArray());
        var run = drafted.GetProperty("runs")[1];
        Assert.Equal(("plan", "brief", "succeeded", 1), (run.GetProperty("agent").GetString(), run.GetProperty("kind").GetString(), run.GetProperty("status").GetString(), run.GetProperty("attempts").GetInt32()));
        Assert.Equal((812, 240), (run.GetProperty("inputTokens").GetInt32(), run.GetProperty("outputTokens").GetInt32()));
        Assert.Equal("brief", drafted.GetProperty("actions").GetProperty("requestApproval").GetString());

        var request = Assert.Single(_world.Plan.Requests);
        var opportunity = request.GetProperty("opportunity");
        Assert.Equal("radar", opportunity.GetProperty("source").GetString());
        Assert.StartsWith("Searches for Monsoon footwear are rising.", opportunity.GetProperty("summary").GetString());
        Assert.Equal("Shoes > Rain", opportunity.GetProperty("category").GetString());
        Assert.Equal(2, request.GetProperty("products").GetArrayLength());

        var activity = await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId);
        var latest = activity.GetProperty("entries")[1];
        Assert.Equal(("stage_run.succeeded", "agent", "Plan agent"), (latest.GetProperty("action").GetString(), latest.GetProperty("actorType").GetString(), latest.GetProperty("actorName").GetString()));
    }

    [Fact]
    public async Task Progress_and_changes_are_pushed_as_live_updates()
    {
        var roomId = await _world.OpenWithBriefAsync();

        var rows = await OutboxRows.ReadAsync(_world.Database, _world.BrandId);
        var progress = rows
            .Where(row => row.Type == "agent.progress")
            .Select(row => JsonDocument.Parse(row.Payload).RootElement)
            .Where(row => row.GetProperty("agent").GetString() == "plan")
            .ToList();
        Assert.Equal(["queued", "running", "queued", "succeeded", "running", "succeeded"], progress.Select(row => row.GetProperty("status").GetString()));
        Assert.All(progress, row => Assert.Equal(roomId, row.GetProperty("subject").GetProperty("id").GetGuid()));
        Assert.Contains(rows, row => row.Type == "campaign.changed" && JsonDocument.Parse(row.Payload).RootElement.GetProperty("campaignId").GetGuid() == roomId);
        Assert.Contains(rows, row => row.Type == "campaigns.changed");
    }

    [Fact]
    public async Task A_failing_agent_is_retried_with_backoff_then_the_room_needs_attention()
    {
        OperationsMail.Enable(_world.Api.Services, _world.Worker.Services);
        _world.Plan.Fails = true;
        var roomId = await _world.AcceptAsync();

        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(0, await _world.RunStagesAsync());
        Assert.Empty(await NotificationRows.EmailsAsync(_world.Database, _world.BrandId, "Operations"));
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Single(await NotificationRows.EmailsAsync(_world.Database, _world.BrandId, "Operations"));
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Equal(0, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await _world.RunStagesAsync());

        var failed = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(3, _world.Plan.Requests.Count);
        Assert.Single(await NotificationRows.EmailsAsync(_world.Database, _world.BrandId, "Operations"));
        Assert.Equal("needs_attention", Campaigns.Status(failed));
        var run = Assert.Single(failed.GetProperty("runs").EnumerateArray());
        Assert.Equal(("failed", 3), (run.GetProperty("status").GetString(), run.GetProperty("attempts").GetInt32()));
        Assert.Equal("The agent couldn't finish this step. Try again.", run.GetProperty("error").GetString());
        Assert.Equal(run.GetProperty("id").GetGuid(), failed.GetProperty("actions").GetProperty("retryRun").GetGuid());
        Assert.Null(failed.GetProperty("actions").GetProperty("requestApproval").GetString());

        _world.Plan.Fails = false;
        using var retried = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"stage-runs/{run.GetProperty("id").GetGuid()}/retry", new { });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("agent_working", Campaigns.Status(await retried.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());

        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal("in_progress", Campaigns.Status(drafted));
        Assert.Equal(["succeeded", "succeeded", "failed"], drafted.GetProperty("runs").EnumerateArray().Select(row => row.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Only_the_last_failed_run_can_be_retried_and_only_by_a_marketer()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var run = (await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("runs")[0].GetProperty("id").GetGuid();

        using var succeeded = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"stage-runs/{run}/retry", new { });
        using var unknown = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"stage-runs/{Guid.NewGuid()}/retry", new { });
        using var approver = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, $"stage-runs/{run}/retry", new { });

        Assert.Equal(HttpStatusCode.Conflict, succeeded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
    }

    [Fact]
    public async Task A_run_whose_room_was_discarded_meanwhile_saves_nothing()
    {
        var roomId = await _world.AcceptAsync();
        _world.Plan.WhileDrafting = () =>
        {
            var detail = Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId).GetAwaiter().GetResult();
            using var discarded = Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "discard", new { version = Campaigns.Version(detail) }).GetAwaiter().GetResult();
            Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        };

        Assert.Equal(1, await _world.RunStagesAsync());

        var room = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(("discarded", "discarded"), (Campaigns.Stage(room), Campaigns.Status(room)));
        Assert.Equal(JsonValueKind.Null, room.GetProperty("brief").ValueKind);
        Assert.Equal("cancelled", room.GetProperty("runs")[0].GetProperty("status").GetString());
        Assert.Equal(0, await CountAsync("Briefs", roomId));
    }

    [Fact]
    public async Task A_trigger_brief_seeds_the_full_brief_and_the_dates_stay_inside_the_moment()
    {
        var roomId = await _world.AcceptAsync();
        var ends = _world.Api.Clock.GetUtcNow().AddDays(3).ToString("O");
        await AddTriggerAsync(_world.Database, roomId, $$"""
            {"source":"weather","summary":"Heavy rain in Mumbai","regions":[{"id":"mumbai","name":"Mumbai"}],"conditionEndsAt":"{{ends}}"}
            """, """
            {"title":"Rain brief","goal":"Sell","audience":"Commuters","keyMessage":"Stay dry","offer":null,"regions":["Mumbai"],"productIds":[],"channels":["search"],"timing":"This week","model":"gemini-trigger","promptVersion":"plan-trigger-brief/1"}
            """);

        Assert.Equal(1, await _world.RunStagesAsync());

        var request = Assert.Single(_world.Plan.Requests);
        Assert.Equal("weather", request.GetProperty("opportunity").GetProperty("source").GetString());
        Assert.Equal(["Mumbai"], request.GetProperty("opportunity").GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()));
        Assert.Equal("Rain brief", request.GetProperty("seed").GetProperty("title").GetString());
        var latest = request.GetProperty("dates").GetProperty("latestEnd").GetString();
        Assert.Equal(latest, request.GetProperty("dates").GetProperty("suggestedEnd").GetString());

        var brief = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("brief");
        Assert.Equal(("drafted", latest), (brief.GetProperty("origin").GetString(), brief.GetProperty("endDate").GetString()));
        Assert.Equal(["Mumbai"], brief.GetProperty("regions").EnumerateArray().Select(region => region.GetString()));
    }

    [Fact]
    public async Task A_room_whose_products_sold_out_fails_without_retrying()
    {
        var roomId = await _world.AcceptAsync();
        await SetEvidenceAsync(_world.Database, roomId, """{"topic":"Monsoon footwear","products":[{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","title":"Gone"}]}""");

        Assert.Equal(1, await _world.RunStagesAsync());

        var run = (await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("runs")[0];
        Assert.Equal(("failed", 1), (run.GetProperty("status").GetString(), run.GetProperty("attempts").GetInt32()));
        Assert.Equal("None of this campaign's products are in stock, so Kairo can't draft a brief.", run.GetProperty("error").GetString());
        Assert.Empty(_world.Plan.Requests);
    }

    [Fact]
    public async Task A_run_left_by_a_stopped_instance_is_picked_up_after_its_lease()
    {
        var roomId = await _world.AcceptAsync();
        await ClaimAsync(roomId, _world.Api.Clock.GetUtcNow().AddMinutes(10));

        Assert.Equal(0, await _world.RunStagesAsync());
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await _world.RunStagesAsync());

        var run = (await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("runs")[1];
        Assert.Equal(("succeeded", 2), (run.GetProperty("status").GetString(), run.GetProperty("attempts").GetInt32()));
    }

    [Fact]
    public async Task The_worker_job_finishes_runs_that_are_due()
    {
        var roomId = await _world.AcceptAsync();
        _world.Worker.Clock.SetUtcNow(_world.Api.Clock.GetUtcNow().AddSeconds(1));

        using var response = await _world.Worker.RunStageRunsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("processed").GetInt32());
        Assert.All(
            (await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("runs").EnumerateArray(),
            run => Assert.Equal("succeeded", run.GetProperty("status").GetString()));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static async Task SetEvidenceAsync(string database, Guid roomId, string evidence)
    {
        await using var connection = new SqlConnection(database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE campaign_rooms.Rooms SET Evidence = @evidence WHERE Id = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        command.Parameters.AddWithValue("@evidence", evidence);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task AddTriggerAsync(string database, Guid roomId, string trigger, string brief)
    {
        await using var connection = new SqlConnection(database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE campaign_rooms.Rooms
            SET Evidence = JSON_MODIFY(JSON_MODIFY(Evidence, '$.trigger', JSON_QUERY(@trigger)), '$.brief', JSON_QUERY(@brief))
            WHERE Id = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        command.Parameters.AddWithValue("@trigger", trigger);
        command.Parameters.AddWithValue("@brief", brief);
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task ClaimAsync(Guid roomId, DateTimeOffset until)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE campaign_rooms.StageRuns SET Status = N'running', Attempts = 1, ClaimedUntil = @until WHERE RoomId = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        command.Parameters.AddWithValue("@until", until);
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<int> CountAsync(string table, Guid roomId)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM campaign_rooms.{table} WHERE RoomId = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
