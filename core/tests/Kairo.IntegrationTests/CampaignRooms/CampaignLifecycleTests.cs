using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CampaignLifecycleTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task Gate_one_asks_the_approver_and_approving_starts_creating()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(drafted) });

        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("brief_draft", "waiting_approval"), (Campaigns.Stage(pending), Campaigns.Status(pending)));
        var approval = pending.GetProperty("pendingApproval");
        Assert.Equal(("brief", "Brief approval"), (approval.GetProperty("gate").GetString(), approval.GetProperty("label").GetString()));
        Assert.Equal([_world.Approver.UserId], approval.GetProperty("approvers").EnumerateArray().Select(person => person.GetProperty("id").GetGuid()));
        Assert.Equal(_world.Brand.Marketer.UserId, approval.GetProperty("requestedBy").GetProperty("id").GetGuid());
        var asked = Assert.Single(await NotificationsAsync(), row => row.Kind == "approval.needed");
        Assert.Equal((_world.Approver.UserId, $"/{_world.Brand.Handle}/campaigns/{roomId}"), (asked.UserId, asked.Link));
        var approverView = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal("brief", approverView.GetProperty("actions").GetProperty("approve").GetString());
        Assert.Equal(["brief_draft"], approverView.GetProperty("actions").GetProperty("sendBackTo").EnumerateArray().Select(stage => stage.GetString()));

        using var approved = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending), comment = "Looks good" });

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var creating = await approved.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("creating", "agent_working"), (Campaigns.Stage(creating), Campaigns.Status(creating)));
        Assert.Equal(JsonValueKind.Null, creating.GetProperty("pendingApproval").ValueKind);
        Assert.Equal(["done", "current"], creating.GetProperty("tracker").GetProperty("stages").EnumerateArray().Take(2).Select(step => step.GetProperty("progress").GetString()));
        Assert.Equal(_world.Api.Clock.GetUtcNow(), creating.GetProperty("tracker").GetProperty("enteredAt").GetDateTimeOffset());
        Assert.NotNull(Assert.Single(await NotificationsAsync(), row => row.Kind == "approval.needed").ResolvedAt);
        var activity = (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["campaign_room.approved", "campaign_room.approval_requested", "stage_run.succeeded", "stage_run.succeeded", "campaign_room.opened"], activity.Select(entry => entry.GetProperty("action").GetString()));
        Assert.Equal("Approved a campaign step", activity[0].GetProperty("label").GetString());
        Assert.Equal("Looks good", activity[0].GetProperty("details").GetProperty("details").GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Only_the_assigned_approver_can_approve_a_gate()
    {
        var other = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        await _world.AssignApproversAsync(other.UserId, other.UserId, other.UserId);
        var roomId = await _world.OpenWithBriefAsync();
        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(drafted) });
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Token);

        using var wrong = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending) });
        using var marketer = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending) });
        using var right = await Campaigns.PostAsync(other.Browser, _world.BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending) });

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("You aren't an approver for this step.", await Campaigns.TitleAsync(wrong));
        Assert.Equal(HttpStatusCode.Forbidden, marketer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal([other.UserId], (await NotificationsAsync()).Where(row => row.Kind == "approval.needed").Select(row => row.UserId));
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var stale = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(drafted) - 1 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("This campaign changed since you opened it. Refresh and try again.", await Campaigns.TitleAsync(stale));
        Assert.Equal("in_progress", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task Approval_waits_for_the_brief()
    {
        var roomId = await _world.AcceptAsync();
        var queued = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var early = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(queued) });
        using var wrongGate = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "launch", version = Campaigns.Version(queued) });

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("Wait for the agent to finish before asking for approval.", await Campaigns.TitleAsync(early));
        Assert.Equal(HttpStatusCode.Conflict, wrongGate.StatusCode);
        Assert.Null(queued.GetProperty("actions").GetProperty("requestApproval").GetString());
    }

    [Fact]
    public async Task Sending_a_brief_back_keeps_it_in_draft_and_tells_the_owner()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var drafted = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(drafted) });
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Token);

        using var noComment = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "brief_draft", comment = " ", version = Campaigns.Version(pending) });
        using var sent = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "brief_draft", comment = "  Lead with quick-dry.  ", version = Campaigns.Version(pending) });

        Assert.Equal(HttpStatusCode.BadRequest, noComment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var returned = await sent.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("brief_draft", "in_progress"), (Campaigns.Stage(returned), Campaigns.Status(returned)));
        var sendBack = returned.GetProperty("lastSendBack");
        Assert.Equal(("brief_draft", "brief_draft", "Lead with quick-dry."), (sendBack.GetProperty("from").GetString(), sendBack.GetProperty("to").GetString(), sendBack.GetProperty("comment").GetString()));
        Assert.Equal(_world.Approver.UserId, sendBack.GetProperty("by").GetProperty("id").GetGuid());
        Assert.Equal(1, returned.GetProperty("brief").GetProperty("version").GetInt32());
        var rows = await NotificationsAsync();
        var told = Assert.Single(rows, row => row.Kind == "work.returned");
        Assert.Equal((_world.Brand.Marketer.UserId, "Lead with quick-dry."), (told.UserId, told.Body));
        Assert.NotNull(Assert.Single(rows, row => row.Kind == "approval.needed").ResolvedAt);
    }

    [Fact]
    public async Task Assets_are_reviewed_then_sent_back_or_approved_through_to_launch()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);

        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var review = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        Assert.Equal(("asset_review", "waiting_approval"), (Campaigns.Stage(review), Campaigns.Status(review)));
        Assert.Equal(JsonValueKind.Null, review.GetProperty("pendingApproval").GetProperty("requestedBy").ValueKind);
        Assert.Equal(["creating", "brief_draft"], review.GetProperty("actions").GetProperty("sendBackTo").EnumerateArray().Select(stage => stage.GetString()));

        using var back = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "send-back", new { to = "creating", comment = "The video is too long.", version = Campaigns.Version(review) });
        Assert.Equal("creating", Campaigns.Stage(await back.Content.ReadFromJsonAsync<JsonElement>(Token)));
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.AssetsChecked));
        var again = await Campaigns.DetailAsync(_world.Approver.Browser, _world.BrandId, roomId);
        using var assets = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "assets", version = Campaigns.Version(again) });
        var ready = await assets.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("ready_to_launch", Campaigns.Stage(ready));
        Assert.Equal("launch", (await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("actions").GetProperty("requestApproval").GetString());

        using var launchRequest = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "launch", version = Campaigns.Version(ready) });
        var waiting = await launchRequest.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var launch = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "launch", version = Campaigns.Version(waiting) });
        Assert.Equal("launching", Campaigns.Stage(await launch.Content.ReadFromJsonAsync<JsonElement>(Token)));

        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.LaunchFailed));
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.LaunchFinished));
        Assert.Equal(CampaignAdvance.WrongState, await _world.SignalAsync(roomId, CampaignSignals.LearningsSaved));
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.TestEnded));
        Assert.Equal(CampaignAdvance.Moved, await _world.SignalAsync(roomId, CampaignSignals.LearningsSaved));
        Assert.Equal(CampaignAdvance.NotFound, await _world.SignalAsync(Guid.NewGuid(), CampaignSignals.TestEnded));

        var closed = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        Assert.Equal(("closed", "closed"), (Campaigns.Stage(closed), Campaigns.Status(closed)));
        Assert.All(closed.GetProperty("tracker").GetProperty("stages").EnumerateArray(), step => Assert.Equal("done", step.GetProperty("progress").GetString()));
        Assert.False(closed.GetProperty("actions").GetProperty("discard").GetBoolean());
        var advanced = (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "campaign_room.advanced")
            .ToList();
        Assert.Equal(6, advanced.Count);
        Assert.All(advanced, entry => Assert.Equal(("agent", "Check agent"), (entry.GetProperty("actorType").GetString(), entry.GetProperty("actorName").GetString())));
        Assert.Equal(4, (await NotificationsAsync()).Count(row => row.Kind == "approval.needed" && row.ResolvedAt is not null));
    }

    [Fact]
    public async Task A_campaign_can_be_discarded_before_launch_with_a_reason()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        using var approver = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "discard", new { version = Campaigns.Version(detail) });
        using var tooLong = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "discard", new { reason = new string('x', 501), version = Campaigns.Version(detail) });
        using var discarded = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "discard", new { reason = " Stock ran low ", version = Campaigns.Version(detail) });

        Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        var room = await discarded.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("discarded", "discarded", "Stock ran low"), (Campaigns.Stage(room), Campaigns.Status(room), room.GetProperty("discardReason").GetString()));
        Assert.Equal("current", room.GetProperty("tracker").GetProperty("stages")[0].GetProperty("progress").GetString());

        using var twice = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "discard", new { version = Campaigns.Version(room) });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("Only a campaign that hasn't launched can be discarded.", await Campaigns.TitleAsync(twice));
    }

    [Fact]
    public async Task Moves_are_pushed_as_live_updates()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);

        var changes = (await OutboxRows.ReadAsync(_world.Database, _world.BrandId))
            .Where(row => row.Type == "campaign.changed")
            .Select(row => JsonDocument.Parse(row.Payload).RootElement.GetProperty("campaignId").GetGuid())
            .ToList();
        Assert.Equal(5, changes.Count);
        Assert.All(changes, id => Assert.Equal(roomId, id));
    }

    [Fact]
    public async Task Every_member_can_read_a_room_but_other_workspaces_cannot()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var adminOnly = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "admin");
        var outsider = await Workspaces.SignInAsync(_world.Api);
        var stranger = await Workspaces.CreateAsync(outsider.Browser);

        var admin = await adminOnly.Browser.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}", Token);
        var outsiderRoom = await outsider.Browser.GetAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}", Token);
        var crossBrand = await outsider.Browser.GetAsync($"{Campaigns.Base(stranger)}/{roomId}", Token);
        var crossActivity = await outsider.Browser.GetAsync($"{Campaigns.Base(stranger)}/{roomId}/activity", Token);

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsiderRoom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossBrand.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossActivity.StatusCode);
        var adminView = await admin.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Null(adminView.GetProperty("actions").GetProperty("requestApproval").GetString());
        Assert.False(adminView.GetProperty("actions").GetProperty("discard").GetBoolean());
    }

    [Theory]
    [InlineData("approval-requests", "gate")]
    [InlineData("approve", "version")]
    [InlineData("send-back", "to")]
    public async Task Bad_requests_say_what_is_missing(string action, string field)
    {
        var roomId = await _world.OpenWithBriefAsync();
        var browser = action == "approval-requests" ? _world.Marketer : _world.Approver.Browser;

        using var response = await Campaigns.PostAsync(browser, _world.BrandId, roomId, action, action == "approve" ? new { gate = "brief" } : new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private Task<IReadOnlyList<NotificationRow>> NotificationsAsync() => NotificationRows.NotificationsAsync(_world.Database, _world.BrandId);
}
