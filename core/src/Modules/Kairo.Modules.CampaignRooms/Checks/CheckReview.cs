using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed class CheckReview(
    CampaignRoomsDbContext db,
    RoomWorkflow workflow,
    StageQueue queue,
    AssetStore store,
    CheckLedger ledger,
    CheckViews views,
    IAuditLog audit,
    TimeProvider time,
    CurrentMember member)
{
    public const string NotReviewing = "Flags can be overridden only while the assets wait for approval.";
    public const string NotFlagged = "This asset has no open flags to override.";
    public const string NotOverridable = "Some of this asset's issues can't be overridden. Send it back instead.";
    public const string NotIdle = "Checks can be run only while Kairo is creating the assets and no agent is working.";
    public const string NothingToCheck = "There are no assets to check yet.";

    public static string? CheckBlocker(Room room) =>
        room.State != RoomStates.Creating || room.PendingGate is not null || (room.RunStatus is { } status && StageRunStatuses.IsActive(status))
            ? NotIdle
            : null;

    public async Task<AssetResult<AssetView>> OverrideAsync(Guid roomId, Guid assetId, int version, string reason, CancellationToken cancellationToken)
    {
        if (await workflow.LockedAsync(roomId, cancellationToken) is not { } room)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotFound);
        }

        var asset = await db.Assets.SingleOrDefaultAsync(
            row => row.Id == assetId && row.RoomId == roomId && row.Status == AssetStatuses.Active && !row.Hidden,
            cancellationToken);
        if (asset is null)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotFound);
        }

        if (room.State != RoomStates.AssetReview || room.PendingGate != RoomGates.Assets)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, NotReviewing);
        }

        if (!await workflow.MayApproveAsync(room.BrandId, RoomGates.Assets, ApprovalRoutes.Approvers, member, cancellationToken))
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, "You aren't an approver for this step.");
        }

        if (asset.CurrentVersion != version)
        {
            return AssetResult<AssetView>.Failed(AssetError.Stale, AssetEditor.StaleAsset);
        }

        var result = await ledger.LatestAsync(asset.Id, version, cancellationToken);
        var issues = result?.ReadIssues() ?? [];
        if (asset.CheckState != AssetCheckStates.Failed || result is null || issues.Count == 0)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, NotFlagged);
        }

        if (issues.Any(issue => !issue.Overridable))
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, NotOverridable);
        }

        result.OverriddenBy = member.UserId;
        result.OverriddenAt = time.GetUtcNow();
        result.OverrideReason = reason;
        asset.CheckState = AssetCheckStates.Overridden;
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "asset.flag_overridden",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                new { assetId = asset.Id, version, state = AssetCheckStates.Failed },
                new { assetId = asset.Id, asset.Kind, version, reason, rules = issues.Select(issue => issue.Rule).Distinct().ToArray(), score = result.Score }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        var current = (await store.CurrentAsync(asset, cancellationToken))!;
        var check = await views.ForAsync(room.Id, room.BrandId, [(asset, current)], cancellationToken);
        return AssetResult<AssetView>.Done(AssetViews.View(asset, current, room.BrandId, check[asset.Id], true));
    }

    public async Task<MoveResult> RequestCheckAsync(Guid roomId, int version, CancellationToken cancellationToken)
    {
        if (await workflow.LockedAsync(roomId, cancellationToken) is not { } room)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (room.Version != version)
        {
            return new MoveResult(room, MoveError.Stale, "This campaign changed since you opened it. Refresh and try again.");
        }

        if (CheckBlocker(room) is { } blocker)
        {
            return MoveResult.Failed(MoveError.NotAllowed, blocker);
        }

        var assets = await db.Assets
            .Where(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden)
            .ToListAsync(cancellationToken);
        if (assets.Count == 0)
        {
            return MoveResult.Failed(MoveError.NotAllowed, NothingToCheck);
        }

        foreach (var asset in assets)
        {
            asset.CheckState = AssetCheckStates.Unchecked;
        }

        var run = await queue.EnqueueAsync(room, StageKinds.Check, member.UserId, cancellationToken);
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("assets.check_requested", RoomWorkflow.AuditTarget, room.Id.ToString(), null, new { runId = run.Id, assets = assets.Count }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return MoveResult.Done(room);
    }
}
