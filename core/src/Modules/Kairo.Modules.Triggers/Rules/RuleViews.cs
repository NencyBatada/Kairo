using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;

namespace Kairo.Modules.Triggers.Rules;

internal sealed record PlaceView(string Id, string Name, string Kind, string? StateId)
{
    public static PlaceView Of(string id) => IndiaRegions.Find(id) is { } region ? Of(region) : new PlaceView(id, id, RegionKinds.State, null);

    public static PlaceView Of(Region region) => new(region.Id, region.Name, region.Kind, region.StateId);
}

internal sealed record ConditionView(string? Kind, double? Threshold, int? WithinDays, Guid? EventId, string? EventName, int? DaysBefore, long? MinUnits)
{
    public static ConditionView Of(RuleCondition condition, FestivalEvent? festival) => new(
        condition.Kind,
        condition.Threshold,
        condition.WithinDays,
        condition.EventId,
        festival?.Name,
        condition.DaysBefore,
        condition.MinUnits);
}

internal sealed record RuleResults(int Fired, int Created, int Joined, int Accepted, double? Lift);

internal sealed record RuleView(
    Guid Id,
    string Name,
    string Source,
    ConditionView Condition,
    PlaceView[] Regions,
    string Category,
    int CooldownHours,
    string Status,
    string Origin,
    string? Evidence,
    string Description,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset? LastCheckedAt,
    string? LastCheckOutcome,
    RuleResults Results,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static RuleView Of(TriggerRule rule, FestivalEvent? festival, RuleResults results) => new(
        rule.Id,
        rule.Name,
        rule.Source,
        ConditionView.Of(rule.Condition, festival),
        rule.Regions.Select(PlaceView.Of).ToArray(),
        rule.Category,
        rule.CooldownHours,
        rule.Status,
        rule.Origin,
        rule.Evidence,
        RuleText.Describe(rule.Source, rule.Condition, rule.Regions, rule.Category, rule.CooldownHours, festival),
        rule.LastFiredAt,
        rule.LastCheckedAt,
        rule.LastCheckOutcome,
        results,
        rule.CreatedAt,
        rule.UpdatedAt);
}

internal sealed record FiringView(
    Guid Id,
    PlaceView[] Regions,
    DateTimeOffset FiredAt,
    DateTimeOffset CooldownUntil,
    DateTimeOffset ConditionEndsAt,
    DateTimeOffset? EndedAt,
    string Summary,
    string Outcome,
    Guid? OpportunityId,
    string? OpportunityStatus)
{
    public static FiringView Of(TriggerFiring firing, string? opportunityStatus) => new(
        firing.Id,
        firing.Regions.Select(PlaceView.Of).ToArray(),
        firing.FiredAt,
        firing.CooldownUntil,
        firing.ConditionEndsAt,
        firing.EndedAt,
        firing.Summary,
        firing.Outcome,
        firing.OpportunityId,
        opportunityStatus);
}

internal sealed record RuleDetail(RuleView Rule, FiringView[] Firings);

internal sealed record RulePreview(string Description);

internal sealed record WeatherKindView(string Kind, string Unit, double Default, double Min, double Max);

internal sealed record TriggerOptions(
    PlaceView[] Regions,
    string[] Categories,
    WeatherKindView[] WeatherKinds,
    int DefaultWithinDays,
    int MaxWithinDays,
    int DefaultDaysBefore,
    int MaxDaysBefore,
    int DefaultCooldownHours,
    int MaxCooldownHours,
    int MaxRegions);
