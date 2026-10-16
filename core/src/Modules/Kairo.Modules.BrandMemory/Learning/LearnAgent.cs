using System.Text.Json;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.BrandMemory.Learning;

internal sealed record LearnProduct(string Id, string Title, string? Category);

internal sealed record LearnCampaign(
    string Title,
    string Kpi,
    string? Objective,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> KeyMessages,
    IReadOnlyList<string> Languages,
    IReadOnlyList<LearnProduct> Products);

internal sealed record LearnResult(
    string Outcome,
    double? Lift,
    double? Low,
    double? High,
    double? Extra,
    double? ExtraLow,
    double? ExtraHigh,
    decimal? CostPerExtra,
    decimal Spend,
    double Results);

internal sealed record LearnRegion(string Name, bool Test, bool Conclusive, double? Lift, double? Low, double? High, double? Extra);

internal sealed record LearnAd(
    string Id,
    string CampaignType,
    string? Language,
    IReadOnlyList<string> Headlines,
    IReadOnlyList<string> Descriptions,
    string? Visual,
    long Impressions,
    long Clicks,
    double Conversions,
    decimal Cost);

internal sealed record LearnRequest(
    string Brand,
    string Currency,
    LearnCampaign Campaign,
    LearnResult Result,
    IReadOnlyList<LearnRegion> Regions,
    IReadOnlyList<LearnAd> Ads);

internal sealed record LearnedLesson(
    string? Kind,
    string? Outcome,
    string? Title,
    string? Lesson,
    IReadOnlyList<string>? AdIds,
    IReadOnlyList<string>? Regions,
    IReadOnlyList<string>? Languages,
    JsonElement? Evidence);

internal sealed record LearnAnswer(IReadOnlyList<LearnedLesson?>? Lessons, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed class LearnAgent(AgentClient agents)
{
    public const string Route = "learn/lessons";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(55);

    public Task<LearnAnswer> LearnAsync(LearnRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<LearnRequest, LearnAnswer>(Route, request, Timeout, cancellationToken);
}
