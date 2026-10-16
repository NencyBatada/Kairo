using System.Text.Json;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record CheckRuleData(string Id, string Name, string Description, string[] AppliesTo);

internal sealed record CustomRuleData(string Id, string Text, string AppliesTo);

internal sealed record TextCheckAsset(string Id, string Kind, AssetTextData Content);

internal sealed record CopyCheckRequest(
    string Brand,
    string Language,
    CopyBriefData Brief,
    IReadOnlyList<CopyProductData> Products,
    JsonElement? BrandKit,
    IReadOnlyList<CheckRuleData> Rules,
    IReadOnlyList<CustomRuleData> CustomRules,
    IReadOnlyList<TextCheckAsset> Assets);

internal sealed record ImageCheckRequest(
    string Brand,
    string Language,
    CopyBriefData Brief,
    IReadOnlyList<CopyProductData> Products,
    JsonElement? BrandKit,
    IReadOnlyList<CheckRuleData> Rules,
    IReadOnlyList<CustomRuleData> CustomRules,
    string AssetId,
    string? Format,
    MediaPhoto Image,
    IReadOnlyList<MediaReferenceProduct> References);

internal sealed record VideoCheckInput(string MimeType, byte[] Data);

internal sealed record VideoCheckLine(string Voiceover, string Caption, decimal Start, decimal End);

internal sealed record VideoCheckRequest(
    string Brand,
    string Language,
    CopyBriefData Brief,
    IReadOnlyList<CopyProductData> Products,
    JsonElement? BrandKit,
    IReadOnlyList<CheckRuleData> Rules,
    IReadOnlyList<CustomRuleData> CustomRules,
    string AssetId,
    VideoCheckInput Video,
    decimal? Seconds,
    IReadOnlyList<VideoCheckLine> Script,
    IReadOnlyList<MediaReferenceProduct> References,
    MediaPhoto? Logo);

internal sealed record CheckFinding(string? Rule, string? Field, int? Index, double? At, string? Message, string? Evidence);

internal sealed record CheckAssetFindings(string? AssetId, IReadOnlyList<CheckFinding?>? Findings);

internal sealed record CheckAnswer(IReadOnlyList<CheckAssetFindings?>? Results, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed class CheckAgent(AgentClient agents)
{
    public const string CopyRoute = "check/copy";
    public const string ImageRoute = "check/image";
    public const string VideoRoute = "check/video";
    public const string BadVideo = "bad_video";

    public static readonly TimeSpan CopyTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan VideoTimeout = TimeSpan.FromSeconds(150);

    public Task<CheckAnswer> CopyAsync(CopyCheckRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<CopyCheckRequest, CheckAnswer>(CopyRoute, request, CopyTimeout, cancellationToken);

    public Task<CheckAnswer> ImageAsync(ImageCheckRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<ImageCheckRequest, CheckAnswer>(ImageRoute, request, ImageTimeout, cancellationToken);

    public Task<CheckAnswer> VideoAsync(VideoCheckRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<VideoCheckRequest, CheckAnswer>(VideoRoute, request, VideoTimeout, cancellationToken);
}
