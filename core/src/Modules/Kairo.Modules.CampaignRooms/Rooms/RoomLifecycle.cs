using Kairo.Contracts.CampaignRooms;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal static class RoomStates
{
    public const string BriefDraft = "brief_draft";
    public const string Creating = "creating";
    public const string AssetReview = "asset_review";
    public const string ReadyToLaunch = "ready_to_launch";
    public const string Launching = "launching";
    public const string Live = "live";
    public const string ResultsReady = "results_ready";
    public const string Closed = "closed";
    public const string Discarded = "discarded";

    public static readonly string[] Stages = [BriefDraft, Creating, AssetReview, ReadyToLaunch, Launching, Live, ResultsReady, Closed];

    public static readonly string[] All = [.. Stages, Discarded];

    public static int Order(string state) => Array.IndexOf(Stages, state);
}

internal static class RoomGates
{
    public const string Brief = "brief";
    public const string Assets = "assets";
    public const string Launch = "launch";

    public static readonly string[] All = [Brief, Assets, Launch];
}

internal static class RoomStatuses
{
    public const string InProgress = "in_progress";
    public const string AgentWorking = "agent_working";
    public const string WaitingApproval = "waiting_approval";
    public const string NeedsAttention = "needs_attention";
    public const string Closed = "closed";
    public const string Discarded = "discarded";

    public static readonly string[] All = [InProgress, AgentWorking, WaitingApproval, NeedsAttention, Closed, Discarded];
}

internal static class RoomLifecycle
{
    public static string? GateFor(string state) => state switch
    {
        RoomStates.BriefDraft => RoomGates.Brief,
        RoomStates.AssetReview => RoomGates.Assets,
        RoomStates.ReadyToLaunch => RoomGates.Launch,
        _ => null,
    };

    public static string? AfterApproval(string gate) => gate switch
    {
        RoomGates.Brief => RoomStates.Creating,
        RoomGates.Assets => RoomStates.ReadyToLaunch,
        RoomGates.Launch => RoomStates.Launching,
        _ => null,
    };

    public static bool MarketerRequests(string gate) => gate is RoomGates.Brief or RoomGates.Launch;

    public static IReadOnlyList<string> SendBackTargets(string state, string? pendingGate) => state switch
    {
        RoomStates.BriefDraft when pendingGate == RoomGates.Brief => [RoomStates.BriefDraft],
        RoomStates.AssetReview => [RoomStates.Creating, RoomStates.BriefDraft],
        RoomStates.ReadyToLaunch => [RoomStates.AssetReview, RoomStates.Creating, RoomStates.BriefDraft],
        _ => [],
    };

    public static bool CanDiscard(string state) =>
        RoomStates.Order(state) is >= 0 and < 4;

    public static (string From, string To)? Signal(string signal) => signal switch
    {
        CampaignSignals.AssetsChecked => (RoomStates.Creating, RoomStates.AssetReview),
        CampaignSignals.LaunchFinished => (RoomStates.Launching, RoomStates.Live),
        CampaignSignals.LaunchFailed => (RoomStates.Launching, RoomStates.Launching),
        CampaignSignals.TestEnded => (RoomStates.Live, RoomStates.ResultsReady),
        CampaignSignals.LearningsSaved => (RoomStates.ResultsReady, RoomStates.Closed),
        _ => null,
    };

    public static string StatusOf(string state, string? pendingGate, string? activeRun) => state switch
    {
        RoomStates.Closed => RoomStatuses.Closed,
        RoomStates.Discarded => RoomStatuses.Discarded,
        _ when activeRun is StageRunStatuses.Queued or StageRunStatuses.Running => RoomStatuses.AgentWorking,
        _ when activeRun is StageRunStatuses.Failed => RoomStatuses.NeedsAttention,
        _ when pendingGate is not null => RoomStatuses.WaitingApproval,
        _ => RoomStatuses.InProgress,
    };
}

internal static class StageRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsActive(string status) => status is Queued or Running;
}
