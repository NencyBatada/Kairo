using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed class ProofPlanDirectory(CampaignRoomsDbContext db) : IProofPlans
{
    public async Task<CurrentProofPlan?> GetCurrentAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var plan = await ProofPlanReader.LatestAsync(db, roomId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var changes = await db.ProofPlans.AsNoTracking()
            .Where(row => row.RoomId == roomId && row.ChangedAfterLock && row.ChangeReason != null)
            .OrderBy(row => row.Version)
            .Select(row => new ProofPlanChange(row.Version, row.ChangeReason!, row.ChangedBy, row.CreatedAt))
            .ToListAsync(cancellationToken);
        var content = plan.Read();
        return new CurrentProofPlan(
            roomId,
            plan.Version,
            content.Unit,
            content.Kpi,
            content.InsideArea,
            [.. content.TestRegions.Select(Region)],
            [.. content.Holdouts.Select(Region)],
            content.StartDate,
            content.EndDate,
            content.MinDetectableLift,
            [.. content.Warnings.Select(warning => warning.Code)],
            plan.LockedAt,
            plan.ApprovedWithWarnings,
            plan.ChangedAfterLock,
            changes);
    }

    private static ProofPlanRegion Region(PlanRegion region) => new(region.Id, region.Name, region.State);
}
