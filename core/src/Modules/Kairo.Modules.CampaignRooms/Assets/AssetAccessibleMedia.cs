using Kairo.Contracts.Media;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed class AssetAccessibleMedia(CampaignRoomsDbContext db, CurrentMember member) : IAccessibleMedia
{
    public string Kind => "asset";
    public async Task<AccessibleMediaSource?> ResolveAsync(MediaTarget target, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id && row.RoomId == target.CampaignId
            && !row.Hidden && row.Status == AssetStatuses.Active, cancellationToken);
        if (asset is null || target.Version != asset.CurrentVersion) return null;
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(row => row.AssetId == asset.Id && row.Version == target.Version, cancellationToken);
        return version?.Read().Media is { } media
            ? new(media.FileKey, media.ContentType, asset.Language == "zxx" ? "en" : asset.Language, member.Has(BrandRoles.Marketer), asset.DurationSeconds, asset.RoomId)
            : null;
    }
}
