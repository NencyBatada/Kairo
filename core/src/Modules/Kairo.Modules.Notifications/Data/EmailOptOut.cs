namespace Kairo.Modules.Notifications.Data;

internal sealed class EmailOptOut : IBrandOwned
{
    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
