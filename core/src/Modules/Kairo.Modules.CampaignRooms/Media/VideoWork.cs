using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed record LanguageVideo(string Language, List<GeneratedAsset> Assets);

internal sealed record VideoDraft(int BriefVersion, IReadOnlyList<string> Voiced, List<GeneratedAsset>? Footage, List<LanguageVideo> Videos);

internal sealed class VideoWork(
    CampaignRoomsDbContext db,
    MediaDesk desk,
    VideoMaker maker,
    AssetStore store,
    IAuditLog audit) : IStageWork
{
    public const int Attempts = 2;

    public static readonly string[] Kinds = [AssetKinds.Video, AssetKinds.Voiceover, AssetKinds.Footage];

    public static readonly string[] SpokenKinds = [AssetKinds.Video, AssetKinds.Voiceover];

    public string Kind => StageKinds.CreateVideo;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public int MaxAttempts => Attempts;

    public string? NextKind(StageRun run, StageOutcome outcome) => StageKinds.Check;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(room, cancellationToken) is not { } context)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to make a video from.");
        }

        var voiced = context.Languages.Where(CreativeLanguages.Speaks).ToList();
        if (voiced.Count == 0)
        {
            return new StageOutcome(
                new VideoDraft(context.Brief.Version, voiced, null, []),
                null,
                null,
                null,
                "None of the brief's languages has a voiceover yet, so Kairo made no video.");
        }

        var locked = await db.Assets.AsNoTracking()
            .Where(asset => asset.RoomId == room.Id && SpokenKinds.Contains(asset.Kind) && asset.Locked && asset.Status == AssetStatuses.Active && voiced.Contains(asset.Language))
            .ToListAsync(cancellationToken);
        var lockedVideos = locked.Where(asset => asset.Kind == AssetKinds.Video).Select(asset => asset.Language).ToHashSet(StringComparer.Ordinal);
        var wanted = voiced.Where(language => !lockedVideos.Contains(language)).ToList();
        if (wanted.Count == 0)
        {
            return new StageOutcome(new VideoDraft(context.Brief.Version, voiced, null, []), null, null, null, "Every video is locked, so Kairo kept them.");
        }

        var voices = new List<VideoVoice>();
        foreach (var language in wanted)
        {
            voices.Add(new VideoVoice(
                language,
                await desk.BusinessNameAsync(context, language, room.Id, cancellationToken),
                await LockedScriptAsync(locked, language, cancellationToken)));
        }

        var footage = await maker.FootageAsync(room, context, null, cancellationToken);
        var setting = await maker.SettingAsync(context, cancellationToken);
        var composed = await Task.WhenAll(voices.Select(voice => maker.ComposeAsync(room, context, setting, voice, footage.Clips, null, cancellationToken)));
        var videos = voices.Zip(composed).Select(pair => new LanguageVideo(pair.First.Language, [pair.Second.Video, pair.Second.Voiceover])).ToList();
        var names = string.Join(" and ", wanted.Select(CreativeLanguages.Name));
        return new StageOutcome(
            new VideoDraft(context.Brief.Version, voiced, [.. footage.Assets], videos),
            footage.Model ?? composed[0].Model,
            footage.PromptVersion ?? composed[0].PromptVersion,
            MediaDesk.Sum([footage.Usage, .. composed.Select(video => video.Usage)]),
            $"Made the {names} video with voiceover and captions");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var draft = (VideoDraft)outcome.Output;
        var (created, updated, removed, kept) = (0, 0, 0, 0);
        void Count(SavedAssets saved) =>
            (created, updated, removed, kept) = (created + saved.Created, updated + saved.Updated, removed + saved.Removed, kept + saved.KeptLocked);

        if (draft.Footage is { } footage)
        {
            Count(await store.SaveGeneratedAsync(room, draft.BriefVersion, CreativeLanguages.None, [AssetKinds.Footage], footage, run, outcome, cancellationToken));
            removed += await store.RetireAsync(room, [AssetKinds.Footage], [CreativeLanguages.None], cancellationToken);
        }

        foreach (var video in draft.Videos)
        {
            Count(await store.SaveGeneratedAsync(room, draft.BriefVersion, video.Language, SpokenKinds, video.Assets, run, outcome, cancellationToken));
        }

        removed += await store.RetireAsync(room, SpokenKinds, draft.Voiced, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "asset.generated",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                null,
                new { runId = run.Id, run.Kind, languages = draft.Videos.Select(video => video.Language).ToArray(), Created = created, Updated = updated, Removed = removed, KeptLocked = kept },
                AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
            cancellationToken);
    }

    private async Task<IReadOnlyList<AssetScriptLine>?> LockedScriptAsync(IReadOnlyList<Asset> locked, string language, CancellationToken cancellationToken)
    {
        var voiceover = locked.FirstOrDefault(asset => asset.Kind == AssetKinds.Voiceover && asset.Language == language);
        return voiceover is not null && await store.CurrentAsync(voiceover, cancellationToken) is { } version
            ? version.Read().Script
            : null;
    }
}
