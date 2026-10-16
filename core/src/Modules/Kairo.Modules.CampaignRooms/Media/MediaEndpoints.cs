using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.CampaignRooms.Media;

internal static class MediaEndpoints
{
    public const string NothingToSkip = "Only a video that failed can be skipped.";

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["video/mp4"] = "mp4",
        ["audio/mpeg"] = "mp3",
    };

    public static async Task<Results<FileStreamHttpResult, NotFound>> FileAsync(
        Guid campaignId,
        Guid assetId,
        int? version,
        CampaignRoomsDbContext db,
        IFileStore store,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == assetId && row.RoomId == campaignId && !row.Hidden,
            cancellationToken);
        if (asset is null)
        {
            return TypedResults.NotFound();
        }

        var wanted = version ?? asset.CurrentVersion;
        var row = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.AssetId == asset.Id && entry.Version == wanted,
            cancellationToken);
        if (row?.Read().Media is not { } media)
        {
            return TypedResults.NotFound();
        }

        Stream stream;
        try
        {
            stream = await store.OpenAsync(FileBuckets.GeneratedMedia, media.FileKey, cancellationToken);
        }
        catch (StoredFileMissingException)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        http.Response.Headers.CacheControl = "private, max-age=3600";
        var name = $"{asset.Kind}-{asset.Format ?? asset.Language}-v{wanted}.{Extensions.GetValueOrDefault(media.ContentType, "bin")}";
        return TypedResults.Stream(stream, media.ContentType, name, enableRangeProcessing: true);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult>> SkipVideoAsync(
        Guid campaignId,
        CampaignRoomsDbContext db,
        RoomWorkflow workflow,
        StageQueue queue,
        IAuditLog audit,
        CampaignReader reader,
        CancellationToken cancellationToken)
    {
        if (await workflow.LockedAsync(campaignId, cancellationToken) is not { } room)
        {
            return TypedResults.NotFound();
        }

        var latest = await queue.LatestAsync(room.Id, room.State, cancellationToken);
        if (room.State != RoomStates.Creating || latest is not { Status: StageRunStatuses.Failed, Kind: StageKinds.CreateVideo })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: NothingToSkip);
        }

        var run = await db.StageRuns.SingleAsync(row => row.Id == latest.Id, cancellationToken);
        run.Status = StageRunStatuses.Cancelled;
        room.RunStatus = StageRunStatuses.Succeeded;
        if (queue.Find(StageKinds.Check) is not null)
        {
            await queue.EnqueueAsync(room, StageKinds.Check, run.RequestedBy, cancellationToken);
        }

        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("asset.video_skipped", RoomWorkflow.AuditTarget, room.Id.ToString(), new { runId = run.Id, run.ErrorCode }, null),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(room, cancellationToken));
    }
}
