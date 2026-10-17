using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class CampaignAskResource(CampaignRoomsDbContext db, CampaignReader reader) : IAskResource
{
    public string Kind => "campaign";
    public BrandRoles Editors => BrandRoles.None;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id, ct);
        return room is null ? null : new(room.Title, $"campaigns/{room.Id}", AskJson.Element(await reader.DetailAsync(room, ct)), AskJson.Revision(room.Version));
    }
    public Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct) => throw new AskResourceException("Use brief or asset previews to edit campaign drafts.", 403);
    public Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct) => throw new AskResourceException("Chat cannot approve or launch campaigns.", 403);
}
