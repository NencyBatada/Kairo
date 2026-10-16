using System.Text.Json;

namespace Kairo.Contracts.Launch;

public interface ICampaignChanges
{
    Task<IReadOnlyList<CampaignState>> ReadAsync(Guid roomId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<GoogleRecommendation>> GoogleRecommendationsAsync(Guid roomId, CancellationToken cancellationToken);

    Task SetBudgetsAsync(Guid roomId, IReadOnlyList<BudgetSetting> budgets, CancellationToken cancellationToken);

    Task SetTargetAsync(Guid roomId, string campaignType, string biddingStrategy, decimal target, CancellationToken cancellationToken);

    Task ApplyGoogleAsync(Guid roomId, string resourceName, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdResult>> AdResultsAsync(Guid roomId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record CampaignState(
    string CampaignType,
    string Status,
    DateOnly End,
    decimal TotalBudget,
    decimal SpentToDate,
    string BiddingStrategy,
    decimal? Target,
    decimal Cost,
    decimal Conversions,
    decimal ConversionValue,
    string? Campaign = null);

public sealed record AdAsset(Guid Id, string Kind, string Language, string? Format, JsonElement Content);

public sealed record AdResult(
    string Key,
    string CampaignType,
    string Language,
    IReadOnlyList<AdAsset> Assets,
    long Impressions,
    long Clicks,
    decimal Cost,
    decimal Conversions,
    decimal ConversionValue);

public sealed record BudgetSetting(string CampaignType, decimal Total);

public sealed record GoogleImpact(decimal BaseCost, decimal PotentialCost, decimal BaseConversions, decimal PotentialConversions,
    decimal BaseConversionValue, decimal PotentialConversionValue, long BaseClicks, long PotentialClicks);

public sealed record GoogleRecommendation(string ResourceName, string Type, string CampaignType, GoogleImpact Impact, JsonElement Details);

public sealed class CampaignChangeException(string message, string fix, bool transient = false) : Exception(message)
{
    public string Fix { get; } = fix;

    public bool Transient { get; } = transient;
}
