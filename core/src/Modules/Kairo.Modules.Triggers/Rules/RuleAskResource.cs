using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Contracts.Opportunities;
using Kairo.Modules.Triggers.Data;

namespace Kairo.Modules.Triggers.Rules;

internal sealed class RuleAskResource(TriggersDbContext db, IOpportunities opportunities, CurrentMember member,
    IAuditLog audit, ILiveUpdates live, TimeProvider time) : IAskResource
{
    public string Kind => "trigger_rule";
    public BrandRoles Editors => BrandRoles.Admin;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var rule = await RuleAsync(target, ct);
        return rule is null ? null : new(rule.Name, $"triggers/{rule.Id}", AskJson.Element(Input(rule)),
            AskJson.Revision(new { input = Input(rule), rule.Status, rule.UpdatedAt }));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        var before = await ReadAsync(target, ct) ?? throw new AskResourceException("This rule is unavailable.", 404);
        var merged = AskJson.Merge(before.Data, changes, "name", "source", "condition", "regions", "category", "cooldownHours");
        var (draft, errors) = await RuleInput.CheckAsync(AskJson.Read<RuleRequest>(merged), db, ct);
        if (draft is null) throw new AskResourceException(JsonSerializer.Serialize(errors), 400);
        var normalized = new TriggerRule();
        draft.ApplyTo(normalized);
        var after = AskJson.Element(Input(normalized));
        return new(before.Data, after, after, ["The rule keeps its current active or paused state. An active rule uses this definition for future checks."]);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        var id = target.Id!.Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM triggers.TriggerRules WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND BrandId = {member.BrandId}", ct);
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        AskJson.Result(await RuleEndpoints.UpdateAsync(id, AskJson.Read<RuleRequest>(payload), db, opportunities, member, audit, live, time, ct));
    }
    private Task<TriggerRule?> RuleAsync(AskTarget target, CancellationToken ct) => db.BrandRules.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id, ct);
    private static RuleRequest Input(TriggerRule rule) => new(rule.Name, rule.Source,
        new ConditionRequest(rule.Condition.Kind, rule.Condition.Threshold, rule.Condition.WithinDays, rule.Condition.EventId, rule.Condition.DaysBefore, rule.Condition.MinUnits),
        [.. rule.Regions], rule.Category, rule.CooldownHours);
}
