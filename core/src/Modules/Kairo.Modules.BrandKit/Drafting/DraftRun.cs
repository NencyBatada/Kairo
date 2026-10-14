namespace Kairo.Modules.BrandKit.Drafting;

internal enum DraftRunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}

internal sealed class DraftRun : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string WebsiteUrl { get; set; } = "";

    public DraftRunStatus Status { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string Pages { get; set; } = "[]";

    public string Ads { get; set; } = "[]";

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

    public Guid RequestedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? VersionNumber { get; set; }
}
