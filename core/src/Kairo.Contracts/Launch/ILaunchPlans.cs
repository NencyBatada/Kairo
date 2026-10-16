using System.Text.Json;

namespace Kairo.Contracts.Launch;

public interface ILaunchPlans
{
    Task<LaunchPlan?> GetAsync(Guid roomId, CancellationToken cancellationToken);

    Task<bool> IsApprovedAsync(Guid roomId, CancellationToken cancellationToken);
}

public sealed record LaunchChannel(string CampaignType, decimal Budget, string BiddingStrategy, decimal? Target = null);

public sealed record LaunchSettings(LaunchChannel[] Channels, string[]? AudienceIds = null, string? SalesCountry = null,
    Dictionary<string, string>? GeoTargetIds = null);

public sealed record LaunchRegion(string Id, string Name);

public sealed record LaunchAsset(Guid Id, int Version, string Kind, string CampaignType, string Language,
    string? AdGroup, string? Format, string[] ProductIds, JsonElement Content, string CheckState, JsonElement Check,
    string? FileKey, string? ContentType, long Bytes, bool SyntheticMedia, string? DisclosureBlocker);

public sealed record LaunchPlan(Guid RoomId, Guid BrandId, string Title, int BriefVersion, int ProofPlanVersion,
    decimal Budget, string Currency, string TimeZone, DateOnly Start, DateOnly End, string[] Languages,
    LaunchRegion[] Regions, LaunchRegion[] Holdouts, string? GoogleAdsAccountId, string? MerchantAccountId,
    string? YouTubeChannelId, string[] ProductIds, string[] LandingPages, LaunchSettings Settings, LaunchAsset[] Assets);
