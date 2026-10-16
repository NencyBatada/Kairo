namespace Kairo.Contracts.CampaignRooms;

public interface IProofPlans
{
    Task<CurrentProofPlan?> GetCurrentAsync(Guid roomId, CancellationToken cancellationToken);
}

public sealed record ProofPlanRegion(string Id, string Name, string? State);

public sealed record ProofPlanChange(int Version, string Reason, Guid? ChangedBy, DateTimeOffset ChangedAt);

public sealed record CurrentProofPlan(
    Guid RoomId,
    int Version,
    string Unit,
    string Kpi,
    bool InsideArea,
    IReadOnlyList<ProofPlanRegion> TestRegions,
    IReadOnlyList<ProofPlanRegion> Holdouts,
    DateOnly? Start,
    DateOnly? End,
    double? MinDetectableLift,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? LockedAt,
    bool ApprovedWithWarnings,
    bool ChangedAfterLock,
    IReadOnlyList<ProofPlanChange> Changes);
