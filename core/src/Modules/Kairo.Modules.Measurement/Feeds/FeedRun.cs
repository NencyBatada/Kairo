namespace Kairo.Modules.Measurement.Feeds;

internal sealed class FeedRun : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Feed { get; set; } = "";

    public string? ExternalId { get; set; }

    public string Status { get; set; } = "";

    public string? Reason { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public int Rows { get; set; }

    public string? Tables { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
