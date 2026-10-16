namespace Kairo.Modules.Measurement.Impact;

internal sealed class ImpactReport : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public Guid ReadingId { get; set; }

    public string Status { get; set; } = ImpactStatuses.Pending;

    public int Attempts { get; set; }

    public string? Problem { get; set; }

    public string Title { get; set; } = "";

    public string Kpi { get; set; } = "";

    public string Currency { get; set; } = "";

    public decimal Spend { get; set; }

    public decimal Results { get; set; }

    public decimal? CostPerResult { get; set; }

    public decimal? CostPerExtra { get; set; }

    public decimal? CostPerExtraLow { get; set; }

    public decimal? CostPerExtraHigh { get; set; }

    public decimal? TargetCost { get; set; }

    public string Outcome { get; set; } = "";

    public string NextStep { get; set; } = "";

    public string? NextTest { get; set; }

    public string Flags { get; set; } = "[]";

    public string? Summary { get; set; }

    public string? NextStepReasoning { get; set; }

    public string? InconclusiveReason { get; set; }

    public string? NextTestAdvice { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }

    public int Version { get; set; } = 1;
}

internal static class ImpactStatuses
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

internal static class ImpactFlags
{
    public const string ChangedAfterLock = "changed_after_lock";
    public const string ApprovedWithWarnings = "approved_with_warnings";
    public const string EndedEarly = "ended_early";
    public const string AccountChange = "account_change";
    public const string ShortWindow = "short_window";
}
