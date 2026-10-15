using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class RoomOpener(CampaignRoomsDbContext db, StageQueue stages, RoomWorkflow workflow, IAuditLog audit, TimeProvider time) : ICampaignRooms
{
    public const string AuditTarget = RoomWorkflow.AuditTarget;
    public const int MaxTitleLength = 200;

    public async Task<Guid> OpenAsync(RoomOpening opening, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var evidence = opening.Evidence.GetRawText();
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Title = opening.Title.Length > MaxTitleLength ? opening.Title[..MaxTitleLength] : opening.Title,
            OpportunityId = opening.OpportunityId,
            Evidence = evidence,
            Products = [.. RoomEvidence.Read(evidence).RoomProducts()],
            OwnerId = opening.OpenedBy,
            CreatedBy = opening.OpenedBy,
            CreatedAt = now,
            StateEnteredAt = now,
            UpdatedAt = now,
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        var run = await stages.EnqueueAsync(room, StageKinds.Brief, opening.OpenedBy, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("campaign_room.opened", AuditTarget, room.Id.ToString(), After: new { room.Title, room.State, room.OpportunityId, runId = run.Id }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return room.Id;
    }
}
