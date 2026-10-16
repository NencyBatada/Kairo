using System.Text.RegularExpressions;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record PlatformLabel(string Platform, string Setting, string Reason, string Source);

internal sealed record AssetDisclosure(
    string Rules,
    bool AiGenerated,
    bool SyntheticMedia,
    string? Watermark,
    IReadOnlyList<PlatformLabel> PlatformLabels,
    string? Blocker);

internal static partial class DisclosureRules
{
    public const string Version = "disclosure/2026-10";
    public const string SynthId = "synthid";
    public const string GoogleAds = "google_ads";
    public const string YouTube = "youtube";
    public const string AiCreated = "ai_created";
    public const string ContainsSyntheticMedia = "contains_synthetic_media";
    public const string GoogleAdsSource = "https://support.google.com/My-Ad-Center-Help/answer/17196133";
    public const string YouTubeSource = "https://developers.google.com/youtube/v3/docs/videos/insert";
    public const string ElectionSource = "https://support.google.com/adspolicy/answer/15142358";
    public const string ImageWatermarkSource = "https://cloud.google.com/use-cases/text-to-image-ai";
    public const string VideoWatermarkSource = "https://deepmind.google/discover/blog/watermarking-ai-generated-text-and-video-with-synthid";
    public const string PoliticalBlocker = "This looks like election or political content. Google needs advertiser verification and a synthetic content disclosure for election ads, and Kairo doesn't make them.";

    private static readonly string[] HindiTerms = ["चुनाव", "मतदान", "मतदाता", "राजनीतिक दल", "लोकसभा", "विधानसभा", "राज्यसभा"];

    public static readonly PlatformLabel GoogleAdsLabel = new(
        GoogleAds,
        AiCreated,
        "Google Ads labels AI-made images and videos, and shows the label on the ad in India, the EU and New York.",
        GoogleAdsSource);

    public static readonly PlatformLabel YouTubeLabel = new(
        YouTube,
        ContainsSyntheticMedia,
        "YouTube asks for realistic AI-made footage and voices to be disclosed with status.containsSyntheticMedia when the video is uploaded.",
        YouTubeSource);

    public static AssetDisclosure For(string kind, AssetContent content) => new(
        Version,
        true,
        !AssetKinds.IsText(kind),
        kind is AssetKinds.Image or AssetKinds.Video or AssetKinds.Footage ? SynthId : null,
        kind switch
        {
            AssetKinds.Image => [GoogleAdsLabel],
            AssetKinds.Video => [GoogleAdsLabel, YouTubeLabel],
            _ => [],
        },
        LooksPolitical(content) ? PoliticalBlocker : null);

    public static bool LooksPolitical(AssetContent content)
    {
        var texts = (content.Keywords ?? []).Select(keyword => keyword.Text)
            .Concat(content.Headlines ?? [])
            .Concat(content.LongHeadlines ?? [])
            .Concat(content.Descriptions ?? [])
            .Concat((content.Script ?? []).SelectMany(line => new[] { line.Voiceover, line.Caption }))
            .Append(content.Title ?? "")
            .Append(content.BusinessName ?? "")
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return texts.Any(text => PoliticalTerms().IsMatch(text) || HindiTerms.Any(term => text.Contains(term, StringComparison.Ordinal)));
    }

    [GeneratedRegex(
        @"\b(elections?|electoral|ballots?|political part(y|ies)|manifesto|polling booths?|lok sabha|vidhan sabha|rajya sabha|election commission)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PoliticalTerms();
}
