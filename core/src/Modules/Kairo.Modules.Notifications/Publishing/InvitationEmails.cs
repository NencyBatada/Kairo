using System.Text.Json;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;

namespace Kairo.Modules.Notifications.Publishing;

internal sealed class InvitationEmails(NotificationsDbContext db, KairoDatabase database, TimeProvider time) : IInvitationEmails
{
    public async Task SendAsync(InvitationEmail invitation, CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Sending an invitation needs a workspace.");
        }

        if (string.IsNullOrWhiteSpace(invitation.To) || invitation.To.Length > 320)
        {
            throw new ArgumentException("An invitation email needs an address of up to 320 characters.", nameof(invitation));
        }

        if (invitation.Roles == BrandRoles.None)
        {
            throw new ArgumentException("An invitation email needs at least one role.", nameof(invitation));
        }

        var now = time.GetUtcNow();
        db.OutboundEmails.Add(new OutboundEmail
        {
            Type = EmailType.Invitation,
            Address = invitation.To,
            Payload = JsonSerializer.Serialize(new InvitationDetails(invitation.InvitedBy, invitation.Roles, invitation.ExpiresAt), JsonSerializerOptions.Web),
            Status = EmailStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed record InvitationDetails(string InvitedBy, BrandRoles Roles, DateTimeOffset ExpiresAt)
{
    public static InvitationDetails Read(string? payload) =>
        JsonSerializer.Deserialize<InvitationDetails>(payload ?? "", JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The invitation email has no details.");
}
