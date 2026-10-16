using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Launch;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.CampaignRooms.LaunchPlans;

internal sealed class ApprovalSnapshots(CampaignRoomsDbContext db, IApprovalLocks approvals, CheckLedger ledger)
{
    public async Task<string> BriefAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var brief = await db.Briefs.AsNoTracking().Where(row => row.RoomId == roomId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        var plan = await db.ProofPlans.AsNoTracking().Where(row => row.RoomId == roomId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        return JsonSerializer.Serialize(new { briefVersion = brief?.Version, brief = brief?.Content.Normalized(), proofPlanVersion = plan?.Version, proofPlan = plan?.Read() }, JsonSerializerOptions.Web);
    }

    public async Task<LaunchAsset[]> AssetsAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var rows = await (from asset in db.Assets.AsNoTracking()
            where asset.RoomId == roomId && asset.Status == AssetStatuses.Active && !asset.Hidden
            join version in db.AssetVersions.AsNoTracking() on new { AssetId = asset.Id, Version = asset.CurrentVersion } equals new { version.AssetId, version.Version }
            select new { asset, version }).ToListAsync(cancellationToken);
        var checks = await ledger.LatestForRoomAsync(roomId, cancellationToken);
        return rows.OrderBy(row => row.asset.Id).Select(row =>
        {
            var content = row.version.Read();
            if (content.Media is { } media && FileKeys.IsDraft(media.FileKey))
            {
                media.FileKey = FileKeys.Kept(media.FileKey);
            }

            var check = checks.GetValueOrDefault((row.asset.Id, row.version.Version));
            var disclosure = DisclosureRules.For(row.asset.Kind, content);
            return new LaunchAsset(row.asset.Id, row.version.Version, row.asset.Kind, row.asset.CampaignType,
                row.asset.Language, row.asset.AdGroup, row.asset.Format, [.. row.asset.ProductIds.Order(StringComparer.Ordinal)],
                JsonSerializer.SerializeToElement(content, JsonSerializerOptions.Web), row.asset.CheckState,
                JsonSerializer.SerializeToElement(check is null ? null : new { check.Id, check.Score, check.Passed, check.Issues, check.SettingsVersion, check.OverriddenBy, check.OverrideReason }, JsonSerializerOptions.Web),
                content.Media?.FileKey, content.Media?.ContentType, content.Media?.Bytes ?? 0, disclosure.SyntheticMedia, disclosure.Blocker);
        }).ToArray();
    }

    public async Task<string> AssetsSnapshotAsync(Guid roomId, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(new { brief = JsonSerializer.Deserialize<JsonElement>(await BriefAsync(roomId, cancellationToken)), assets = await AssetsAsync(roomId, cancellationToken) }, JsonSerializerOptions.Web);

    public async Task<bool> PriorGatesMatchAsync(Guid roomId, CancellationToken cancellationToken) =>
        await approvals.MatchesAsync(roomId, RoomGates.Brief, await BriefAsync(roomId, cancellationToken), cancellationToken)
        && await approvals.MatchesAsync(roomId, RoomGates.Assets, await AssetsSnapshotAsync(roomId, cancellationToken), cancellationToken);
}
