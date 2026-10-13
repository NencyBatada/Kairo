namespace Kairo.Modules.Identity.Invitations;

internal enum InvitationStatus
{
    Pending,
    Accepted,
    Declined,
    Revoked,
}

internal sealed class Invitation : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Email { get; set; } = "";

    public BrandRoles Roles { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public InvitationStatus Status { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }
}
