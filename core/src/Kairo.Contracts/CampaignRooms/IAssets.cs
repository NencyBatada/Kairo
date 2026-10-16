using System.Text.Json;

namespace Kairo.Contracts.CampaignRooms;

public interface IAssets
{
    Task<IReadOnlyList<CampaignAsset>> GetCurrentAsync(Guid roomId, CancellationToken cancellationToken);

    Task<CampaignAsset?> GetVersionAsync(Guid assetId, int version, CancellationToken cancellationToken);
}

public sealed record CampaignAssetFile(string Bucket, string Key, string ContentType, long Bytes);

public sealed record CampaignAssetLabel(string Platform, string Setting, string Reason, string Source);

public sealed record CampaignAssetDisclosure(
    string Rules,
    bool AiGenerated,
    bool SyntheticMedia,
    string? Watermark,
    IReadOnlyList<CampaignAssetLabel> PlatformLabels,
    string? Blocker);

public sealed record CampaignAssetProvenance(
    string Origin,
    string? Model,
    string? PromptVersion,
    IReadOnlyList<string> PromptDigests,
    Guid? StageRunId,
    int BriefVersion,
    int? BrandKitVersion,
    IReadOnlyList<string> PhotoUrls,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt);

public sealed record CampaignAsset(
    Guid Id,
    Guid RoomId,
    string Kind,
    string CampaignType,
    string Language,
    string? AdGroup,
    string? Format,
    int? Width,
    int? Height,
    decimal? DurationSeconds,
    IReadOnlyList<string> ProductIds,
    bool Locked,
    bool Hidden,
    string CheckState,
    int Version,
    bool Current,
    JsonElement Content,
    CampaignAssetFile? File,
    CampaignAssetDisclosure Disclosure,
    CampaignAssetProvenance Provenance);
