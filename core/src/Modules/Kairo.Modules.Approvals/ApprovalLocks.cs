using Kairo.Contracts.Approvals;
using Kairo.Modules.Approvals.Data;

namespace Kairo.Modules.Approvals;

internal sealed class ApprovalLocks(ApprovalsDbContext db, TimeProvider time) : IApprovalLocks
{
    public async Task<ApprovalEvidence> ApproveAsync(Guid roomId, string gate, Guid approverId, string snapshot, CancellationToken cancellationToken)
    {
        var entry = new ApprovalEntry
        {
            RoomId = roomId, Gate = gate, ApproverId = approverId, ApprovedAt = time.GetUtcNow(),
            Snapshot = snapshot, Hash = ApprovalHash.Of(snapshot),
        };
        db.Entries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return new ApprovalEvidence(entry.Id, roomId, gate, approverId, entry.ApprovedAt, entry.Hash, snapshot);
    }

    public async Task<bool> MatchesAsync(Guid roomId, string gate, string snapshot, CancellationToken cancellationToken)
    {
        var entry = await db.Entries.AsNoTracking().Where(row => row.RoomId == roomId && row.Gate == gate)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        return entry is { Invalidated: false } && entry.Hash == ApprovalHash.Of(snapshot);
    }

    public async Task InvalidateAsync(Guid roomId, string[] gates, CancellationToken cancellationToken)
    {
        foreach (var gate in gates)
        {
            db.Entries.Add(new ApprovalEntry { RoomId = roomId, Gate = gate, Invalidated = true, ApprovedAt = time.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
