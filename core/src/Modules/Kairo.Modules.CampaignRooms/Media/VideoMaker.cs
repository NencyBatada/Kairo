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

    public static string FootageKey(string language, int index) => MediaDesk.KeyFor(AssetKinds.Footage, language, $"clip{index}");

    public static bool NeedsNewFootage(string instruction) =>
        instruction.Split([' ', ',', '.', '!', '?', ';', ':', '-', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant())
            .Any(word => VisualWords.Any(visual => word.StartsWith(visual, StringComparison.Ordinal)));

    public async Task<MadeVideo> MakeAsync(Room room, CopyContext context, string? instruction, bool reuseFootage, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var brief = MediaDesk.Brief(context);
        var clips = reuseFootage ? await StoredFootageAsync(room, language, cancellationToken) : [];
        IReadOnlyList<GeneratedAsset> footage = [];
        FootageAnswer? planned = null;
        if (clips.Count == 0)
        {
            var frames = await FramesAsync(room, context, cancellationToken);
            planned = await agent.FootageAsync(
                new FootageRequest(
                    brief,
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
                    FootageKey(language, index),
                    AssetKinds.Footage,
                    BriefChoices.DemandGen,
                    null,
                    ProductIds(context),
                    new AssetContent { Media = media },
                    AssetFormats.Vertical,
                    null,
                    null,
                    (decimal)clip.Seconds,
                    Hidden: true));
            }

            footage = saved;
            clips = [.. made.Select(clip => new StoredClip(clip.Data, (decimal)clip.Seconds))];
        }

        var video = await agent.VideoAsync(
            new VideoRequest(
                brief,
                context.BrandKit,
                await desk.BusinessNameAsync(context, room.Id, cancellationToken),
                language,
                [.. clips.Select(clip => new VideoClip("video/mp4", clip.Data, (double)clip.Seconds))],
                await desk.LogoAsync(context.BrandKit, cancellationToken),
                MediaDesk.Background(context.BrandKit),
                Seconds,
                null,
                instruction),
            cancellationToken);
        if (video.Video is not { Data.Length: > 0 } file || video.Voiceover is not { Data.Length: > 0 } voice)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent didn't return the video.");
        }

        var script = (video.Script ?? [])
            .Select(line => new AssetScriptLine { Voiceover = line.Voiceover, Caption = line.Caption, Start = (decimal)line.Start, End = (decimal)line.End })
            .ToList();
        var videoMedia = await desk.SaveAsync(room.BrandId, file.Data, "video/mp4", cancellationToken);
        var voiceMedia = await desk.SaveAsync(room.BrandId, voice.Data, "audio/mpeg", cancellationToken);
        var products = ProductIds(context);
        return new MadeVideo(
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
                Round(file.Seconds)),
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
                Round(voice.Seconds)),
            footage,
            planned?.Model ?? video.Model,
            planned?.PromptVersion ?? video.PromptVersion,
            MediaDesk.Sum(planned?.Usage, video.Usage));
    }

    private static decimal Round(double seconds) => Math.Round((decimal)seconds, 2);

    private static List<string> ProductIds(CopyContext context) =>
        [.. context.Products.Take(6).Select(entry => entry.Product.Id.ToString())];

    private async Task<List<StoredClip>> StoredFootageAsync(Room room, string language, CancellationToken cancellationToken)
    {
        var clips = new List<StoredClip>();
        foreach (var (asset, content) in await desk.CurrentAsync(room.Id, language, AssetKinds.Footage, cancellationToken))
        {
            if (content.Media is { } media && await desk.ReadAsync(media, cancellationToken) is { } data)
            {
                trail.Used(asset, asset.CurrentVersion, media.FileKey);
                clips.Add(new StoredClip(data, asset.DurationSeconds ?? ClipSeconds));
            }
        }

        return clips.Count == 0 ? [] : clips;
    }

    private async Task<List<FootageFrame>> FramesAsync(Room room, CopyContext context, CancellationToken cancellationToken)
    {
        var frames = new List<FootageFrame>();
        var vertical = (await desk.CurrentAsync(room.Id, context.Language, AssetKinds.Image, cancellationToken))
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
