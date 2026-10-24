namespace Kairo.Contracts.Operations;

public interface IUsageLedger
{
    Task<CampaignUsage> ReadAsync(Guid campaignId, CancellationToken ct);
}

public sealed record GenerationCapacity(int TextCalls, int Images, int VideoClips);
public sealed record UsageTotals(long? InputTokens, long? OutputTokens, long? ThoughtTokens, long? CachedTokens,
    int? GeneratedImages, decimal? VideoSeconds, long? SpeechCharacters, decimal? SpeechSeconds);
public sealed record CampaignUsage(Guid CampaignId, int ProviderCalls, GenerationCapacity Consumed,
    GenerationCapacity Limits, GenerationCapacity Remaining, UsageTotals Totals,
    decimal KnownEstimatedUsd, bool CostComplete, int UnknownCostCalls);
