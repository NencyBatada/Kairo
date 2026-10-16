using Kairo.Modules.CampaignRooms.Briefs;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record AssetKeywordView(string Text, string Match);

internal sealed record AssetScriptLineView(string Voiceover, string Caption, decimal Start, decimal End);

internal sealed record AssetContentView(
    AssetKeywordView[]? Keywords,
    string[]? Headlines,
    string[]? LongHeadlines,
    string[]? Descriptions,
    string? Path1,
    string? Path2,
    string? FinalUrl,
    string? BusinessName,
    string? CallToAction,
    string? Title,
    string? CurrentTitle,
    string? OfferId,
    AssetScriptLineView[]? Script);

internal sealed record PlatformLabelView(string Platform, string Setting, string Reason, string Source);

internal sealed record AssetDisclosureView(
    string Rules,
    bool AiGenerated,
    bool SyntheticMedia,
    string? Watermark,
    PlatformLabelView[] PlatformLabels,
    string? Blocker);

internal sealed record AssetView(
    Guid Id,
    string Kind,
    string CampaignType,
    string Language,
    string? AdGroup,
    string? Format,
    int? Width,
    int? Height,
    decimal? DurationSeconds,
    string[] ProductIds,
    bool Locked,
    string CheckState,
    int Version,
    string Origin,
    bool EditedByPerson,
    string? MediaUrl,
    AssetContentView Content,
    AssetDisclosureView Disclosure,
    DateTimeOffset UpdatedAt);

internal sealed record AssetGroupView(string CampaignType, string Label, AssetView[] Assets);

internal sealed record AssetSetView(
    Guid CampaignId,
    string Stage,
    bool CanEdit,
    string? EditBlocker,
    string[] Languages,
    AssetGroupView[] Groups);

internal sealed record AssetRegenerationView(Guid RunId, AssetView Asset);

internal sealed record AssetKeywordInput(string? Text, string? Match);

internal sealed record AssetEditRequest(
    int? Version,
    List<AssetKeywordInput>? Keywords,
    List<string>? Headlines,
    List<string>? LongHeadlines,
    List<string>? Descriptions,
    string? Path1,
    string? Path2,
    string? BusinessName,
    string? CallToAction,
    string? Title);

internal sealed record AssetVersionRequest(int? Version);

internal sealed record AssetRegenerateRequest(int? Version, string? Instruction);

internal static class AssetViews
{
    public static readonly string[] CampaignTypeOrder = [BriefChoices.Search, BriefChoices.Shopping, BriefChoices.DemandGen];

    public static AssetView View(Asset asset, AssetVersion version, Guid brandId)
    {
        var content = version.Read();
        return new AssetView(
            asset.Id,
            asset.Kind,
            asset.CampaignType,
            asset.Language,
            asset.AdGroup,
            asset.Format,
            asset.Width,
            asset.Height,
            asset.DurationSeconds,
            [.. asset.ProductIds],
            asset.Locked,
            asset.CheckState,
            version.Version,
            version.Origin,
            version.Origin == AssetOrigins.Edited,
            content.Media is null ? null : MediaUrl(brandId, asset.RoomId, asset.Id, version.Version),
            Content(content),
            Disclosure(DisclosureRules.For(asset.Kind, content)),
            asset.UpdatedAt);
    }

    public static AssetDisclosureView Disclosure(AssetDisclosure disclosure) => new(
        disclosure.Rules,
        disclosure.AiGenerated,
        disclosure.SyntheticMedia,
        disclosure.Watermark,
        [.. disclosure.PlatformLabels.Select(label => new PlatformLabelView(label.Platform, label.Setting, label.Reason, label.Source))],
        disclosure.Blocker);

    public static string MediaUrl(Guid brandId, Guid roomId, Guid assetId, int version) =>
        $"/api/brands/{brandId}/campaigns/{roomId}/assets/{assetId}/file?version={version}";

    public static AssetContentView Content(AssetContent content) => new(
        content.Keywords is null ? null : [.. content.Keywords.Select(keyword => new AssetKeywordView(keyword.Text, keyword.Match))],
        content.Headlines?.ToArray(),
        content.LongHeadlines?.ToArray(),
        content.Descriptions?.ToArray(),
        content.Path1,
        content.Path2,
        content.FinalUrl,
        content.BusinessName,
        content.CallToAction,
        content.Title,
        content.CurrentTitle,
        content.OfferId,
        content.Script is null ? null : [.. content.Script.Select(line => new AssetScriptLineView(line.Voiceover, line.Caption, line.Start, line.End))]);

    public static AssetGroupView[] Groups(IEnumerable<AssetView> assets) =>
    [
        .. assets
            .GroupBy(asset => asset.CampaignType)
            .OrderBy(group => Array.IndexOf(CampaignTypeOrder, group.Key) is var index and >= 0 ? index : CampaignTypeOrder.Length)
            .Select(group => new AssetGroupView(
                group.Key,
                BriefChoices.CampaignTypeName(group.Key),
                [
                    .. group
                        .OrderBy(asset => asset.Language, StringComparer.Ordinal)
                        .ThenBy(asset => asset.AdGroup ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(asset => AssetKinds.Order(asset.Kind))
                        .ThenBy(asset => asset.Format ?? "", StringComparer.Ordinal)
                        .ThenBy(asset => asset.Content.Title ?? "", StringComparer.OrdinalIgnoreCase),
                ])),
    ];
}
