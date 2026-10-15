using System.Text.Json;

namespace Kairo.Contracts.CampaignRooms;

public interface ICampaignRooms
{
    Task<Guid> OpenAsync(RoomOpening opening, CancellationToken cancellationToken);
}

public sealed record RoomOpening(Guid OpportunityId, string Title, JsonElement Evidence, Guid OpenedBy);
