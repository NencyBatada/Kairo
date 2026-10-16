using System.Text.Json;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed record MadeVideo(
    GeneratedAsset Video,
    GeneratedAsset Voiceover,
    IReadOnlyList<GeneratedAsset> Footage,
    string? Model,
    string? PromptVersion,
    AgentUsage? Usage);

internal sealed record StoredClip(byte[] Data, decimal Seconds);

internal sealed record MadeFootage(
    IReadOnlyList<StoredClip> Clips,
    IReadOnlyList<GeneratedAsset> Assets,
    string? Model,
    string? PromptVersion,
    AgentUsage? Usage);

internal sealed record VideoSetting(MediaBrief Brief, JsonElement? BrandKit, MediaPhoto? Logo, string? Background);

internal sealed record VideoVoice(string Language, string BusinessName, IReadOnlyList<AssetScriptLine>? Script);

internal sealed record ComposedVideo(GeneratedAsset Video, GeneratedAsset Voiceover, string? Model, string? PromptVersion, AgentUsage? Usage);

internal sealed class VideoMaker(MediaDesk desk, MediaAgent agent, ProvenanceTrail trail)
{
    public const int Clips = 2;
    public const int ClipSeconds = 8;
    public const int Seconds = 15;
    public const int Frames = 2;

    private static readonly string[] VisualWords =
    [
        "visual", "scene", "shot", "footage", "clip", "camera", "background", "setting", "show", "look",
        "angle", "motion", "outdoor", "indoor", "light", "colour", "color", "festive", "rain", "street",
    ];

    public static string VideoKey(string language) => MediaDesk.KeyFor(AssetKinds.Video, language, "vertical");

    public static string VoiceoverKey(string language) => MediaDesk.KeyFor(AssetKinds.Voiceover, language, "voiceover");

    public static string FootageKey(int index) => MediaDesk.KeyFor(AssetKinds.Footage, CreativeLanguages.None, $"clip{index}");

    public static bool NeedsNewFootage(string instruction) =>
        instruction.Split([' ', ',', '.', '!', '?', ';', ':', '-', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant())
            .Any(word => VisualWords.Any(visual => word.StartsWith(visual, StringComparison.Ordinal)));

    public async Task<MadeVideo> MakeAsync(Room room, CopyContext context, VideoVoice voice, string? instruction, bool reuseFootage, CancellationToken cancellationToken)
    {
        var clips = reuseFootage ? await StoredFootageAsync(room, voice.Language, cancellationToken) : [];
        MadeFootage? footage = null;
        if (clips.Count == 0)
        {
            footage = await FootageAsync(room, context, instruction, cancellationToken);
            clips = [.. footage.Clips];
        }

        var setting = await SettingAsync(context, cancellationToken);
        var video = await ComposeAsync(room, context, setting, voice, clips, instruction, cancellationToken);
        return new MadeVideo(
            video.Video,
            video.Voiceover,
            footage?.Assets ?? [],
            footage?.Model ?? video.Model,
            footage?.PromptVersion ?? video.PromptVersion,
            MediaDesk.Sum(footage?.Usage, video.Usage));
    }

    public async Task<MadeFootage> FootageAsync(Room room, CopyContext context, string? instruction, CancellationToken cancellationToken)
    {
        var frames = await FramesAsync(room, context, cancellationToken);
        var planned = await agent.FootageAsync(
            new FootageRequest(
                desk.BriefFor(context),
                context.BrandKit,
                [.. context.Products.Take(6).Select(entry => new FootageProduct(entry.Key, CopyDesk.Cut(entry.Product.Title, 300)))],
                frames,
                Clips,
                ClipSeconds,
                instruction),
            cancellationToken);
        var made = (planned.Clips ?? []).Where(clip => clip.Data.Length > 0).OrderBy(clip => clip.Index).ToList();
        if (made.Count == 0)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent didn't return any video clips.");
        }

        var saved = new List<GeneratedAsset>();
        foreach (var (clip, index) in made.Select((clip, index) => (clip, index)))
        {
            var media = await desk.SaveAsync(room.BrandId, clip.Data, "video/mp4", cancellationToken);
            saved.Add(new GeneratedAsset(
                FootageKey(index),
                AssetKinds.Footage,
                BriefChoices.DemandGen,
                null,
                ProductIds(context),
                new AssetContent { Media = media },
                AssetFormats.Vertical,
                null,
                null,
                (decimal)clip.Seconds,
                Hidden: true,
                Language: CreativeLanguages.None));
        }

        return new MadeFootage(
            [.. made.Select(clip => new StoredClip(clip.Data, (decimal)clip.Seconds))],
            saved,
            planned.Model,
            planned.PromptVersion,
            planned.Usage);
    }

    public async Task<VideoSetting> SettingAsync(CopyContext context, CancellationToken cancellationToken) =>
        new(desk.BriefFor(context), context.BrandKit, await desk.LogoAsync(context.BrandKit, cancellationToken), MediaDesk.Background(context.BrandKit));

    public async Task<ComposedVideo> ComposeAsync(
        Room room,
        CopyContext context,
        VideoSetting setting,
        VideoVoice voice,
        IReadOnlyList<StoredClip> clips,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var language = voice.Language;
        var video = await agent.VideoAsync(
            new VideoRequest(
                setting.Brief,
                setting.BrandKit,
                voice.BusinessName,
                language,
                [.. clips.Select(clip => new VideoClip("video/mp4", clip.Data, (double)clip.Seconds))],
                setting.Logo,
                setting.Background,
                Seconds,
                voice.Script is { Count: > 0 } kept ? [.. kept.Select(line => new VideoScriptLine(line.Voiceover, line.Caption))] : null,
                instruction),
            cancellationToken);
        if (video.Video is not { Data.Length: > 0 } file || video.Voiceover is not { Data.Length: > 0 } spoken)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, $"The Create agent didn't return the {CreativeLanguages.Name(language)} video.");
        }

        var script = (video.Script ?? [])
            .Select(line => new AssetScriptLine { Voiceover = line.Voiceover, Caption = line.Caption, Start = (decimal)line.Start, End = (decimal)line.End })
            .ToList();
        var videoMedia = await desk.SaveAsync(room.BrandId, file.Data, "video/mp4", cancellationToken);
        var voiceMedia = await desk.SaveAsync(room.BrandId, spoken.Data, "audio/mpeg", cancellationToken);
        var products = ProductIds(context);
        return new ComposedVideo(
            new GeneratedAsset(
                VideoKey(language),
                AssetKinds.Video,
                BriefChoices.DemandGen,
                null,
                products,
                new AssetContent { Media = videoMedia, Script = script },
                AssetFormats.Vertical,
                video.Width,
                video.Height,
                Round(file.Seconds),
                Language: language),
            new GeneratedAsset(
                VoiceoverKey(language),
                AssetKinds.Voiceover,
                BriefChoices.DemandGen,
                null,
                products,
                new AssetContent { Media = voiceMedia, Script = script },
                null,
                null,
                null,
                Round(spoken.Seconds),
                Language: language),
            video.Model,
            video.PromptVersion,
            video.Usage);
    }

    public async Task<IReadOnlyList<StoredClip>> StoredFootageAsync(Room room, string language, CancellationToken cancellationToken)
    {
        foreach (var owner in new[] { CreativeLanguages.None, language })
        {
            var clips = new List<StoredClip>();
            foreach (var (asset, content) in await desk.CurrentAsync(room.Id, owner, AssetKinds.Footage, cancellationToken))
            {
                if (content.Media is { } media && await desk.ReadAsync(media, cancellationToken) is { } data)
                {
                    trail.Used(asset, asset.CurrentVersion, media.FileKey);
                    clips.Add(new StoredClip(data, asset.DurationSeconds ?? ClipSeconds));
                }
            }

            if (clips.Count > 0)
            {
                return clips;
            }
        }

        return [];
    }

    private static decimal Round(double seconds) => Math.Round((decimal)seconds, 2);

    private static List<string> ProductIds(CopyContext context) =>
        [.. context.Products.Take(6).Select(entry => entry.Product.Id.ToString())];

    private async Task<List<FootageFrame>> FramesAsync(Room room, CopyContext context, CancellationToken cancellationToken)
    {
        var frames = new List<FootageFrame>();
        var vertical = (await desk.CurrentAsync(room.Id, CreativeLanguages.None, AssetKinds.Image, cancellationToken))
            .Where(entry => entry.Asset.Format == AssetFormats.Vertical && entry.Content.Media is not null);
        foreach (var (asset, content) in vertical)
        {
            if (frames.Count == Frames)
            {
                break;
            }

            if (await desk.ReadAsync(content.Media!, cancellationToken) is { } data)
            {
                trail.Used(asset, asset.CurrentVersion, content.Media!.FileKey);
                frames.Add(new FootageFrame(KeyOf(context, asset), content.Media!.ContentType, data));
            }
        }

        if (frames.Count == 0)
        {
            foreach (var entry in await desk.PhotosAsync(context.Products, Frames, cancellationToken))
            {
                frames.Add(new FootageFrame(entry.Product.Key, entry.Photos[0].MimeType, entry.Photos[0].Data));
            }
        }

        return frames.Count > 0 ? frames : throw MediaDesk.NoPhotos();
    }

    private static string? KeyOf(CopyContext context, Asset asset) =>
        asset.ProductIds.FirstOrDefault() is { } id ? context.KeyOf(id) : null;
}
