using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class RoomOpener(CampaignRoomsDbContext db, IAuditLog audit, TimeProvider time) : ICampaignRooms
{
    public const string AuditTarget = "campaign_room";
    public const int MaxTitleLength = 200;

    public async Task<Guid> OpenAsync(RoomOpening opening, CancellationToken cancellationToken)
    {
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Title = opening.Title.Length > MaxTitleLength ? opening.Title[..MaxTitleLength] : opening.Title,
            OpportunityId = opening.OpportunityId,
            Evidence = opening.Evidence.GetRawText(),
            CreatedBy = opening.OpenedBy,
            CreatedAt = time.GetUtcNow(),
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("campaign_room.opened", AuditTarget, room.Id.ToString(), After: new { room.Title, room.State, room.OpportunityId }),
            cancellationToken);
        return room.Id;
    }
}
