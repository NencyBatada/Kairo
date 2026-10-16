using Kairo.Contracts.Audit;
using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;

namespace Kairo.Modules.Launch;

internal sealed class LaunchRecovery(LaunchDbContext db, KairoDatabase database, LaunchGoogle google, IAuditLog audit) : ILaunchRecovery
{
    public async Task<bool> CanReturnAsync(Guid roomId, CancellationToken cancellationToken) =>
        await db.Launches.AnyAsync(row => row.RoomId == roomId && row.Status == "failed" && !row.Uncertain && row.ClaimToken == null, cancellationToken);

    public async Task<bool> ReturnAsync(Guid roomId, CancellationToken cancellationToken)
    {
        await database.LockAsync($"launch:{roomId:N}", cancellationToken);
        var record = await db.Launches.SingleOrDefaultAsync(row => row.RoomId == roomId && row.Status != "superseded", cancellationToken);
        if (record is not { Status: "failed", Uncertain: false, ClaimToken: null, AttemptingStep: null }) return false;
        var resources = LaunchRunner.Read<Dictionary<string, string>>(record.Resources);
        if (resources.ContainsKey("built"))
        {
            var plan = LaunchRunner.Read<LaunchPlan>(record.Plan);
            string[] statuses;
            try { statuses = await LaunchReconciler.CampaignStatesAsync(google, plan, resources, cancellationToken); }
            catch (LaunchException) { return false; }
            if (!statuses.All(status => status is "PAUSED" or "REMOVED")) return false;
        }
        record.Status = "superseded";
        record.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("launch.returned", "campaign_room", roomId.ToString(), null, new { record.Id, record.Hash }), cancellationToken);
        return true;
    }
}
