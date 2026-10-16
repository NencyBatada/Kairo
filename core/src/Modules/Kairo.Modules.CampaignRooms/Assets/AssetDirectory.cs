using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed class AssetDirectory(CampaignRoomsDbContext db) : IAssets
{
    public async Task<IReadOnlyList<CampaignAsset>> GetCurrentAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var current = await (
            from asset in db.Assets.AsNoTracking()
            where asset.RoomId == roomId && asset.Status == AssetStatuses.Active
            join version in db.AssetVersions.AsNoTracking()
                on new { AssetId = asset.Id, Version = asset.CurrentVersion } equals new { version.AssetId, version.Version }
            select new { asset, version }).ToListAsync(cancellationToken);
        return
        [
            .. current
                .OrderBy(row => row.asset.CampaignType, StringComparer.Ordinal)
                .ThenBy(row => row.asset.Language, StringComparer.Ordinal)
                .ThenBy(row => AssetKinds.Order(row.asset.Kind))
                .ThenBy(row => row.asset.Key, StringComparer.Ordinal)
                .Select(row => Of(row.asset, row.version)),
        ];
    }

    public async Task<CampaignAsset?> GetVersionAsync(Guid assetId, int version, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        var row = await db.AssetVersions.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.AssetId == assetId && entry.Version == version, cancellationToken);
        return row is null ? null : Of(asset, row);
    }

    public static CampaignAsset Of(Asset asset, AssetVersion version)
    {
        var content = version.Read();
        var provenance = version.ReadProvenance();
        var disclosure = DisclosureRules.For(asset.Kind, content);
        using var json = JsonDocument.Parse(version.Content);
        return new CampaignAsset(
            asset.Id,
            asset.RoomId,
            asset.Kind,
            asset.CampaignType,
            asset.Language,
            asset.AdGroup,
            asset.Format,
            asset.Width,
            asset.Height,
            asset.DurationSeconds,
            [.. asset.ProductIds],
            asset.Locked,
            asset.Hidden,
            asset.CheckState,
            version.Version,
            version.Version == asset.CurrentVersion,
            json.RootElement.Clone(),
            content.Media is { } media ? new CampaignAssetFile(FileBuckets.GeneratedMedia, media.FileKey, media.ContentType, media.Bytes) : null,
            new CampaignAssetDisclosure(
                disclosure.Rules,
                disclosure.AiGenerated,
                disclosure.SyntheticMedia,
                disclosure.Watermark,
                [.. disclosure.PlatformLabels.Select(label => new CampaignAssetLabel(label.Platform, label.Setting, label.Reason, label.Source))],
                disclosure.Blocker),
            new CampaignAssetProvenance(
                version.Origin,
                version.Model,
                version.PromptVersion,
                [.. provenance.AgentCalls.Select(call => call.Digest)],
                version.StageRunId,
                version.Provenance is null ? asset.BriefVersion : provenance.BriefVersion,
                provenance.BrandKitVersion,
                [.. provenance.PhotoUrls],
                version.CreatedBy,
                version.CreatedAt,
                version.ApprovedBy,
                version.ApprovedAt));
    }
}
