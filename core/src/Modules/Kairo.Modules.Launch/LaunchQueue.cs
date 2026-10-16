using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;

namespace Kairo.Modules.Launch;

internal sealed class LaunchQueue(LaunchDbContext db, ILaunchPlans plans, TimeProvider time) : ILaunchQueue
{
    public async Task EnqueueAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var plan = await plans.GetAsync(roomId, cancellationToken);
        if (plan is null || !await plans.IsApprovedAsync(roomId, cancellationToken))
        {
            throw new InvalidOperationException("Launch requires approval of the current plan.");
        }

        if (await db.Launches.AnyAsync(row => row.RoomId == roomId && row.Status != "superseded", cancellationToken))
        {
            throw new InvalidOperationException("This campaign already has a launch record.");
        }

        var snapshot = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
        db.Launches.Add(new LaunchRecord
        {
            Id = Guid.NewGuid(), RoomId = roomId, Plan = snapshot, Hash = ApprovalHash.Of(snapshot), CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
