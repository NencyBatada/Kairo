namespace Kairo.Modules.CampaignRooms.Stages;

internal sealed class StageRun : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public string Stage { get; set; } = "";

    public string Agent { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Status { get; set; } = Rooms.StageRunStatuses.Queued;

    public int Attempts { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public Guid? RequestedBy { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Output { get; set; }
}

internal static class StageAgents
{
    public const string Plan = "plan";
    public const string Create = "create";
    public const string Check = "check";
    public const string Prove = "prove";
    public const string Learn = "learn";
}

internal static class StageKinds
{
    public const string StarterBrief = "starter_brief";
}
