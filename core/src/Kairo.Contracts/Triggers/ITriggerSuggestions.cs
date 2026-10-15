using System.Text.Json;

namespace Kairo.Contracts.Triggers;

public interface ITriggerSuggestions
{
    Task<SuggestionResult> SuggestAsync(SuggestedRule rule, CancellationToken cancellationToken);
}

public sealed record SuggestedRule(
    string Name,
    string Source,
    JsonElement Condition,
    IReadOnlyList<string> Regions,
    string Category,
    int? CooldownHours,
    string Evidence,
    Guid? MemoryEntryId);

public sealed record SuggestionResult(Guid? RuleId, IReadOnlyDictionary<string, string[]> Errors);
