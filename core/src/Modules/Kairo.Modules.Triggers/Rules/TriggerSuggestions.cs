using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Contracts.Triggers;
using Kairo.Modules.Triggers.Data;

namespace Kairo.Modules.Triggers.Rules;

internal sealed class TriggerSuggestions(TriggersDbContext db, KairoDatabase database, IAuditLog audit, ILiveUpdates live, TimeProvider time) : ITriggerSuggestions
{
    public async Task<SuggestionResult> SuggestAsync(SuggestedRule rule, CancellationToken cancellationToken)
    {
        ConditionRequest? condition = null;
        if (rule.Condition.ValueKind == JsonValueKind.Object)
        {
            condition = rule.Condition.Deserialize<ConditionRequest>(JsonSerializerOptions.Web);
        }

        var request = new RuleRequest(rule.Name, rule.Source, condition, [.. rule.Regions], rule.Category, rule.CooldownHours);
        var (draft, errors) = await RuleInput.CheckAsync(request, db, cancellationToken);
        var evidence = rule.Evidence?.Trim() ?? "";
        if (evidence.Length is 0 or > RuleLimits.MaxEvidenceLength)
        {
            errors["evidence"] = [$"Give the evidence in 1 to {RuleLimits.MaxEvidenceLength} characters."];
        }

        if (draft is null || errors.Count > 0)
        {
            return new SuggestionResult(null, errors);
        }

        if (await RuleEndpoints.LimitReachedAsync(db, database, db.BrandId, cancellationToken))
        {
            return new SuggestionResult(null, new Dictionary<string, string[]> { ["rules"] = [$"A workspace can have up to {RuleLimits.MaxRules} trigger rules."] });
        }

        var now = time.GetUtcNow();
        var suggested = new TriggerRule
        {
            Id = Guid.NewGuid(),
            Status = RuleStatuses.Draft,
            Origin = RuleOrigins.Suggested,
            Evidence = evidence,
            MemoryEntryId = rule.MemoryEntryId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        draft.ApplyTo(suggested);
        db.Rules.Add(suggested);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "trigger_rule.suggested",
                RuleEndpoints.AuditTarget,
                suggested.Id.ToString(),
                After: new { suggested.Name, suggested.Source, suggested.Status, suggested.Evidence },
                Actor: AuditActor.Agent("Learn agent", "learn")),
            cancellationToken);
        await live.PublishAsync(TriggerLiveEvents.RulesChanged(), cancellationToken);
        return new SuggestionResult(suggested.Id, errors);
    }
}
