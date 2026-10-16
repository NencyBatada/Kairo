using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed class VideoWork(
    CampaignRoomsDbContext db,
    MediaDesk desk,
    VideoMaker maker,
    AssetStore store,
    IAuditLog audit) : IStageWork
{
    public static readonly string[] Kinds = [AssetKinds.Video, AssetKinds.Voiceover, AssetKinds.Footage];

    public string Kind => StageKinds.CreateVideo;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public int MaxAttempts => 2;

    public string? NextKind(StageRun run, StageOutcome outcome) => StageKinds.Check;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(room, cancellationToken) is not { } context)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to make a video from.");
        }

        var lockedVideo = await db.Assets.AsNoTracking().AnyAsync(
            asset => asset.RoomId == room.Id && asset.Language == context.Language && asset.Kind == AssetKinds.Video && asset.Locked && asset.Status == AssetStatuses.Active,
            cancellationToken);
        if (lockedVideo)
        {
            return new StageOutcome(new MediaDraft(context.Brief.Version, context.Language, null, true, 0), null, null, null, "The video is locked, so Kairo kept it.");
        }

        var made = await maker.MakeAsync(room, context, null, false, cancellationToken);
        return new StageOutcome(
            new MediaDraft(context.Brief.Version, context.Language, [made.Video, made.Voiceover, .. made.Footage], true, 0),
            made.Model,
            made.PromptVersion,
            made.Usage,
            $"Made a {made.Video.DurationSeconds:0.#}-second video with voiceover and captions");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var draft = (MediaDraft)outcome.Output;
        if (draft.Assets is null)
        {
            return;
        }

        var saved = await store.SaveGeneratedAsync(room, draft.BriefVersion, draft.Language, Kinds, draft.Assets, run, outcome, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "asset.generated",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                null,
                new { runId = run.Id, run.Kind, language = draft.Language, saved.Created, saved.Updated, saved.Removed, saved.KeptLocked },
                AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
            cancellationToken);
    }
}
