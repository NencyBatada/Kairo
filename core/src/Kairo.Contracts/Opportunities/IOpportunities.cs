namespace Kairo.Contracts.Opportunities;

public interface IOpportunities
{
    Task<TriggerOutcome> RaiseTriggeredAsync(TriggerSignal signal, CancellationToken cancellationToken);

    Task SaveBriefAsync(Guid opportunityId, TriggerBrief? brief, CancellationToken cancellationToken);

    Task ExtendTriggerAsync(Guid opportunityId, Guid firingId, DateTimeOffset conditionEndsAt, CancellationToken cancellationToken);

    Task EndTriggerAsync(Guid opportunityId, Guid firingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TriggeredState>> GetTriggeredAsync(IReadOnlyCollection<Guid> opportunityIds, CancellationToken cancellationToken);
}

public sealed record TriggerRegion(string Id, string Name);

public sealed record TriggerSignal(
    Guid FiringId,
    Guid RuleId,
    string RuleName,
    string Source,
    string Topic,
    string Summary,
    string Category,
    IReadOnlyList<TriggerRegion> Regions,
    DateTimeOffset FiredAt,
    DateTimeOffset ConditionEndsAt);

public static class TriggerOutcomes
{
    public const string Created = "created";
    public const string Joined = "joined";
    public const string NoStock = "no_stock";
}

public sealed record TriggerOutcome(string Outcome, Guid? OpportunityId, string? Topic, IReadOnlyList<Guid> ProductIds);

public sealed record TriggerBrief(
    string Title,
    string Goal,
    string Audience,
    string KeyMessage,
    string? Offer,
    IReadOnlyList<string> Regions,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<string> Channels,
    string Timing,
    string? Model,
    string? PromptVersion,
    DateTimeOffset DraftedAt);

public sealed record TriggeredState(Guid Id, string Topic, string Status, string? BriefStatus, IReadOnlyList<Guid> ProductIds);
