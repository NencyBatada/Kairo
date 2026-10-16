namespace Kairo.Contracts.CampaignRooms;

public interface ICampaignBriefs
{
    Task<CampaignBrief?> GetAsync(Guid roomId, CancellationToken cancellationToken);
}

public sealed record CampaignBrief(
    Guid RoomId,
    int Version,
    string Title,
    string Objective,
    string Kpi,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> KeyMessages,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> ProductIds,
    IReadOnlyList<string> Languages);
