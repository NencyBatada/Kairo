namespace Kairo.Modules.Notifications.Data;

internal enum EmailStatus
{
    Pending,
    Sent,
    Logged,
    Skipped,
    Failed,
}

internal sealed class OutboundEmail : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public long NotificationId { get; set; }

    public InAppNotification? Notification { get; set; }

    public EmailStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
