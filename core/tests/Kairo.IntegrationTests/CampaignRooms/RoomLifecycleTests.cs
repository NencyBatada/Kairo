using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class RoomLifecycleTests
{
    [Theory]
    [InlineData("brief", "creating")]
    [InlineData("assets", "ready_to_launch")]
    [InlineData("launch", "launching")]
    public void Each_gate_moves_the_room_one_stage_on(string gate, string next)
    {
        Assert.Equal(next, RoomLifecycle.AfterApproval(gate));
    }

    [Fact]
    public void Each_approval_stage_has_its_own_gate_and_the_marketer_requests_every_gate()
    {
        Assert.Equal(
            ["brief", null, "assets", "launch", null, null, null, null],
            RoomStates.Stages.Select(RoomLifecycle.GateFor));
        Assert.Equal([true, true, true], RoomGates.All.Select(RoomLifecycle.MarketerRequests));
        Assert.Equal(
            ["brief", "assets", null, "launch", null, null, null, null],
            RoomStates.Stages.Select(RoomLifecycle.RequestFrom));
    }

    [Theory]
    [InlineData("brief_draft", null, new string[0])]
    [InlineData("brief_draft", "brief", new[] { "brief_draft" })]
    [InlineData("creating", null, new string[0])]
    [InlineData("asset_review", "assets", new[] { "creating", "brief_draft" })]
    [InlineData("ready_to_launch", null, new[] { "asset_review", "creating", "brief_draft" })]
    [InlineData("launching", null, new string[0])]
    [InlineData("live", null, new string[0])]
    public void Work_goes_back_only_to_an_earlier_stage_before_launch(string state, string? pending, string[] targets)
    {
        Assert.Equal(targets, RoomLifecycle.SendBackTargets(state, pending));
    }

    [Fact]
    public void Only_a_room_that_has_not_started_launching_can_be_discarded()
    {
        Assert.Equal(
            [true, true, true, true, false, false, false, false, false],
            RoomStates.All.Select(RoomLifecycle.CanDiscard));
    }

    [Theory]
    [InlineData(CampaignSignals.AssetsChecked, "creating", "asset_review")]
    [InlineData(CampaignSignals.LaunchFinished, "launching", "live")]
    [InlineData(CampaignSignals.LaunchFailed, "launching", "launching")]
    [InlineData(CampaignSignals.TestEnded, "live", "results_ready")]
    [InlineData(CampaignSignals.LearningsSaved, "results_ready", "closed")]
    public void System_signals_move_the_room_from_one_stage_to_the_next(string signal, string from, string to)
    {
        Assert.Equal((from, to), RoomLifecycle.Signal(signal));
    }

    [Fact]
    public void An_unknown_signal_moves_nothing()
    {
        Assert.Null(RoomLifecycle.Signal("launch_now"));
    }

    [Theory]
    [InlineData("brief_draft", null, null, "in_progress")]
    [InlineData("brief_draft", null, "queued", "agent_working")]
    [InlineData("brief_draft", null, "running", "agent_working")]
    [InlineData("brief_draft", null, "failed", "needs_attention")]
    [InlineData("brief_draft", "brief", "succeeded", "waiting_approval")]
    [InlineData("asset_review", "assets", null, "waiting_approval")]
    [InlineData("closed", null, "running", "closed")]
    [InlineData("discarded", "brief", null, "discarded")]
    public void The_status_says_who_the_room_is_waiting_on(string state, string? pending, string? run, string status)
    {
        Assert.Equal(status, RoomLifecycle.StatusOf(state, pending, run));
    }

    [Fact]
    public void The_tracker_marks_stages_before_the_current_one_as_done()
    {
        var room = new Room { State = RoomStates.AssetReview, PendingGate = RoomGates.Assets, StateEnteredAt = DateTimeOffset.UnixEpoch };
        var approval = new CampaignApprovalView("assets", "Asset approval", [new(Guid.NewGuid(), "Priya"), new(Guid.NewGuid(), "Sam")], null, DateTimeOffset.UnixEpoch, "approvers", null);

        var tracker = CampaignReader.Tracker(room, null, approval, "Asha");

        Assert.Equal(
            ["done", "done", "current", "upcoming", "upcoming", "upcoming", "upcoming", "upcoming"],
            tracker.Stages.Select(step => step.Progress));
        Assert.Equal(("approvers", "Priya, Sam"), (tracker.OwnerKind, tracker.Owner));
        Assert.Equal("Priya, Sam to review the assets and approve it or send it back.", tracker.NextAction);
    }

    [Fact]
    public void The_tracker_names_the_agent_while_it_works_and_the_owner_when_it_fails()
    {
        var room = new Room { State = RoomStates.BriefDraft };
        var working = new StageRun { Agent = StageAgents.Plan, Kind = StageKinds.Brief, Status = StageRunStatuses.Running };

        var busy = CampaignReader.Tracker(room, working, null, "Asha");
        var stuck = CampaignReader.Tracker(room, new StageRun { Agent = StageAgents.Plan, Status = StageRunStatuses.Failed }, null, "Asha");
        var ready = CampaignReader.Tracker(room, new StageRun { Agent = StageAgents.Plan, Status = StageRunStatuses.Succeeded }, null, "Asha");

        Assert.Equal(("agent", "Plan agent", "The Plan agent is drafting the brief."), (busy.OwnerKind, busy.Owner, busy.NextAction));
        Assert.Equal(("person", "Asha", "The Plan agent couldn't finish. Try it again."), (stuck.OwnerKind, stuck.Owner, stuck.NextAction));
        Assert.Equal(("person", "Asha", "Review the brief and ask for approval."), (ready.OwnerKind, ready.Owner, ready.NextAction));
    }

    [Fact]
    public void A_discarded_room_keeps_the_stage_it_reached()
    {
        var room = new Room { State = RoomStates.Discarded, DiscardedFrom = RoomStates.Creating };

        var tracker = CampaignReader.Tracker(room, null, null, "Asha");

        Assert.Equal(["done", "current", "upcoming"], tracker.Stages.Take(3).Select(step => step.Progress));
        Assert.Equal(("none", "Nothing to do. This campaign was discarded."), (tracker.OwnerKind, tracker.NextAction));
    }
}
