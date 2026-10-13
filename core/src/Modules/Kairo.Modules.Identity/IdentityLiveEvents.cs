using Kairo.Contracts.Live;

namespace Kairo.Modules.Identity;

internal static class IdentityLiveEvents
{
    public static LiveEvent TeamChanged(Guid userId, bool removed) =>
        new("team.changed", new { userId, removed }, LiveAudience.Everyone);

    public static LiveEvent InvitationsChanged() =>
        new("invitations.changed", new { }, LiveAudience.Roles(BrandRoles.Admin));

    public static LiveEvent ApprovalSettingsChanged() =>
        new("approval_settings.changed", new { }, LiveAudience.Everyone);
}
