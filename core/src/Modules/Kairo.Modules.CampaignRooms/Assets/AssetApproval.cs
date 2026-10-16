using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record ApprovedAssets(string? Problem, int Approved, int Kept)
{
    public static ApprovedAssets Failed(string problem) => new(problem, 0, 0);
}

internal sealed class AssetApproval(
    CampaignRoomsDbContext db,
    IFileStore files,
    TimeProvider time,
    ILogger<AssetApproval> logger)
{
    public const int CopiesAtOnce = 4;
    public const string MissingFile = "A media file in this set is missing. Send the assets back so Kairo can make it again.";
    public const string StorageFailed = "Kairo couldn't keep the approved media files. Try approving again.";

    public async Task<string?> RequestBlockerAsync(Room room, CancellationToken cancellationToken)
    {
        if (room.RunStatus is { } status && StageRunStatuses.IsActive(status))
        {
            return "Wait for the agents to finish before asking for asset approval.";
        }

        var states = await db.Assets
            .Where(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden)
            .Select(asset => asset.CheckState)
            .ToListAsync(cancellationToken);
        if (states.Count == 0)
        {
            return "There are no assets to send for approval yet.";
        }

        var waiting = states.Count(state => state == AssetCheckStates.Unchecked);
        return waiting switch
        {
            0 => null,
            1 => "1 asset hasn't been checked by Brand Guardian yet. Run the checks before asking for approval.",
            _ => $"{waiting} assets haven't been checked by Brand Guardian yet. Run the checks before asking for approval.",
        };
    }

    public async Task<string?> BlockerAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var states = await db.Assets
            .Where(asset => asset.RoomId == roomId && asset.Status == AssetStatuses.Active && !asset.Hidden)
            .Select(asset => asset.CheckState)
            .ToListAsync(cancellationToken);
        var waiting = states.Count(state => state == AssetCheckStates.Unchecked);
        var flagged = states.Count(state => state == AssetCheckStates.Failed);
        if (waiting > 0)
        {
            return waiting == 1
                ? "1 asset hasn't been checked by Brand Guardian yet. Send it back so it can be checked."
                : $"{waiting} assets haven't been checked by Brand Guardian yet. Send them back so they can be checked.";
        }

        if (flagged > 0)
        {
            return flagged == 1
                ? "1 asset still has open issues. Override its flag with a reason, or send it back."
                : $"{flagged} assets still have open issues. Override each flag with a reason, or send the assets back.";
        }

        return null;
    }

    public async Task<ApprovedAssets> ApproveAsync(Guid roomId, Guid approverId, CancellationToken cancellationToken)
    {
        if (await BlockerAsync(roomId, cancellationToken) is { } blocker)
        {
            return ApprovedAssets.Failed(blocker);
        }

        var current = await (
            from asset in db.Assets
            where asset.RoomId == roomId && asset.Status == AssetStatuses.Active && !asset.Hidden
            join version in db.AssetVersions
                on new { AssetId = asset.Id, Version = asset.CurrentVersion } equals new { version.AssetId, version.Version }
            select version).ToListAsync(cancellationToken);
        var drafts = current
            .Select(version => (Version: version, Content: version.Read()))
            .Where(entry => entry.Content.Media is { } media && FileKeys.IsDraft(media.FileKey))
            .ToList();
        try
        {
            await Parallel.ForEachAsync(
                drafts,
                new ParallelOptions { MaxDegreeOfParallelism = CopiesAtOnce, CancellationToken = cancellationToken },
                async (entry, token) =>
                {
                    var key = entry.Content.Media!.FileKey;
                    await files.CopyAsync(FileBuckets.GeneratedMedia, key, FileKeys.Kept(key), token);
                });
        }
        catch (StoredFileMissingException exception)
        {
            logger.LogWarning(exception, "An approved media file in room {RoomId} is missing.", roomId);
            return ApprovedAssets.Failed(MissingFile);
        }
        catch (FileStoreException exception)
        {
            logger.LogWarning(exception, "Couldn't keep the approved media files for room {RoomId}.", roomId);
            return ApprovedAssets.Failed(StorageFailed);
        }

        foreach (var (version, content) in drafts)
        {
            content.Media!.FileKey = FileKeys.Kept(content.Media.FileKey);
            version.Content = content.Write();
        }

        var now = time.GetUtcNow();
        foreach (var version in current)
        {
            version.ApprovedBy = approverId;
            version.ApprovedAt = now;
        }

        return new ApprovedAssets(null, current.Count, drafts.Count);
    }
}
