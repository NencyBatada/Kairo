using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Notifications;

public interface IInvitationEmails
{
    Task SendAsync(InvitationEmail invitation, CancellationToken cancellationToken);
}

public sealed record InvitationEmail(string To, string InvitedBy, BrandRoles Roles, DateTimeOffset ExpiresAt);
