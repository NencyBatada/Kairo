namespace Kairo.Modules.Identity.Sessions;

internal sealed class Session
{
    public string Key { get; set; } = "";

    public Guid UserId { get; set; }

    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
