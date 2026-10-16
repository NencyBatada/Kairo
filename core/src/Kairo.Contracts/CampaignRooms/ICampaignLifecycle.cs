using Kairo.Contracts.Audit;

namespace Kairo.Contracts.CampaignRooms;

public interface ICampaignLifecycle
{
    Task<CampaignAdvance> AdvanceAsync(Guid roomId, string signal, AuditActor actor, CancellationToken cancellationToken);
}

public enum CampaignAdvance
{
    Moved,
    NotFound,
    WrongState,
}

public static class CampaignSignals
{
    public const string LaunchStarted = "launch_started";
    public const string AssetsChecked = "assets_checked";
    public const string LaunchFinished = "launch_finished";
    public const string LaunchFailed = "launch_failed";
    public const string TestEnded = "test_ended";
    public const string LearningsSaved = "learnings_saved";
}
