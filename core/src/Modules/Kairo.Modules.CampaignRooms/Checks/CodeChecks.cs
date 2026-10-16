using System.Globalization;
using Kairo.Modules.CampaignRooms.Assets;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record RawFinding(string Rule, string Message, string? Field = null, int? Index = null, double? At = null, string? Evidence = null);

internal static class CodeChecks
{
    public const long MaxImageBytes = 5120 * 1024;
    public const int VideoWidth = 1080;
    public const int VideoHeight = 1920;
    public const decimal MinVideoSeconds = 6;
    public const decimal MaxVideoSeconds = 60;
    public const string PoliticalMessage = "Mentions elections or politics. Kairo doesn't make political ads, so this asset can't be approved.";

    public static readonly IReadOnlyDictionary<string, (int Width, int Height)> ImageSizes = new Dictionary<string, (int, int)>
    {
        [AssetFormats.Landscape] = (1200, 628),
        [AssetFormats.Square] = (1200, 1200),
        [AssetFormats.Portrait] = (960, 1200),
        [AssetFormats.Vertical] = (1080, 1920),
    };

    private static readonly string[] ImageTypes = ["image/jpeg", "image/png"];

    public static string TargetOf(string kind) => kind switch
    {
        AssetKinds.Image => CheckTargets.Image,
        AssetKinds.Video or AssetKinds.Voiceover => CheckTargets.Video,
        _ => CheckTargets.Text,
    };

    public static List<RawFinding> For(Asset asset, AssetContent content)
    {
        var found = new List<RawFinding>();
        if (AssetKinds.IsText(asset.Kind))
        {
            foreach (var (field, messages) in AdLimits.Check(asset.Kind, content))
            {
                found.AddRange(messages.Select(message => new RawFinding(CheckRules.TextLimits, message, field)));
            }
        }
        else
        {
            found.AddRange(Media(asset, content));
        }

        if (DisclosureRules.LooksPolitical(content))
        {
            found.Add(new RawFinding(CheckRules.Political, PoliticalMessage));
        }

        return found;
    }

    private static IEnumerable<RawFinding> Media(Asset asset, AssetContent content)
    {
        if (content.Media is not { } media || string.IsNullOrEmpty(media.FileKey) || media.Bytes <= 0)
        {
            yield return Spec($"The {Noun(asset.Kind)} file is missing. Regenerate it.");
            yield break;
        }

        switch (asset.Kind)
        {
            case AssetKinds.Image:
                if (!ImageTypes.Contains(media.ContentType))
                {
                    yield return Spec("Google Ads needs a JPEG or PNG image.", media.ContentType);
                }

                if (media.Bytes > MaxImageBytes)
                {
                    yield return Spec("The image is larger than Google Ads' 5 MB limit.", $"{media.Bytes / 1024} KB");
                }

                if (asset.Format is { } format && ImageSizes.TryGetValue(format, out var size) && (asset.Width != size.Width || asset.Height != size.Height))
                {
                    yield return Spec(
                        $"A {format} image must be {size.Width}×{size.Height} pixels.",
                        $"{asset.Width?.ToString(CultureInfo.InvariantCulture) ?? "?"}×{asset.Height?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
                }

                break;
            case AssetKinds.Video:
                if (media.ContentType != "video/mp4")
                {
                    yield return Spec("The video must be an MP4 file.", media.ContentType);
                }

                if (asset.Width != VideoWidth || asset.Height != VideoHeight)
                {
                    yield return Spec($"A vertical video must be {VideoWidth}×{VideoHeight} pixels (9:16).", $"{asset.Width}×{asset.Height}");
                }

                if (asset.DurationSeconds is not { } seconds || seconds < MinVideoSeconds || seconds > MaxVideoSeconds)
                {
                    yield return Spec(
                        $"The video must run between {MinVideoSeconds:0} and {MaxVideoSeconds:0} seconds.",
                        asset.DurationSeconds is { } length ? $"{length.ToString("0.#", CultureInfo.InvariantCulture)} s" : null);
                }

                break;
            case AssetKinds.Voiceover:
                if (media.ContentType != "audio/mpeg")
                {
                    yield return Spec("The voiceover must be an MP3 file.", media.ContentType);
                }

                break;
        }
    }

    public static RawFinding BadVideo() => Spec("Brand Guardian couldn't read the video file. Regenerate it.");

    private static RawFinding Spec(string message, string? evidence = null) => new(CheckRules.MediaSpec, message, Evidence: evidence);

    private static string Noun(string kind) => kind switch
    {
        AssetKinds.Video => "video",
        AssetKinds.Voiceover => "voiceover",
        _ => "image",
    };
}
