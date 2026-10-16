using Kairo.Modules.CampaignRooms.Data;
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

    public async Task<ApprovedAssets> ApproveAsync(Guid roomId, Guid approverId, CancellationToken cancellationToken)
    {
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
