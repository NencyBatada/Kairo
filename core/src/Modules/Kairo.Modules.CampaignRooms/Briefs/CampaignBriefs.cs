using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class CampaignBriefs(CampaignRoomsDbContext db) : ICampaignBriefs
{
    public async Task<CampaignBrief?> GetAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var brief = await db.Briefs.AsNoTracking()
            .Where(row => row.RoomId == roomId)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (brief is null)
        {
            return null;
        }

        var content = brief.Content.Normalized();
        return new CampaignBrief(
            roomId,
            brief.Version,
            content.Title,
            content.Objective,
            content.Kpi ?? "",
            [.. content.Audiences ?? []],
            [.. content.KeyMessages ?? []],
            [.. content.Regions ?? []],
            [.. content.ProductIds ?? []],
            [.. content.Languages ?? []]);
    }
}
