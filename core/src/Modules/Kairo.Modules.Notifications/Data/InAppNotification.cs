namespace Kairo.Modules.Notifications.Data;

internal sealed class InAppNotification : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Link { get; set; } = "";

    public string? Subject { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset? RemindAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public bool AwaitsDigest { get; set; }

    public long? DigestEmailId { get; set; }
}
