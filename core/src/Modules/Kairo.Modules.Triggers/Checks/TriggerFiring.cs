namespace Kairo.Modules.Triggers.Checks;

internal sealed class TriggerFiring : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RuleId { get; set; }

    public List<string> Regions { get; set; } = [];

    public DateTimeOffset FiredAt { get; set; }

    public DateTimeOffset CooldownUntil { get; set; }

    public DateTimeOffset ConditionEndsAt { get; set; }

    public string Summary { get; set; } = "";

    public string Details { get; set; } = "{}";

    public Guid? OpportunityId { get; set; }

    public string Outcome { get; set; } = FiringOutcomes.Created;

    public DateTimeOffset? EndedAt { get; set; }

    public bool Blocks(string region, DateTimeOffset now) =>
        Regions.Contains(region, StringComparer.Ordinal) && (EndedAt is null || CooldownUntil > now);
}

internal static class FiringOutcomes
{
    public const string Created = "created";
    public const string Joined = "joined";
}
