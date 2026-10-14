using Kairo.Contracts.Live;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.Publishing;

internal static class Inboxes
{
    public static async Task<HashSet<Guid>> OptedOutAsync(
        NotificationsDbContext db,
        NotificationKind kind,
        IReadOnlyCollection<Guid> users,
        CancellationToken cancellationToken)
    {
        if (!kind.Email || !kind.CanTurnOff || users.Count == 0)
        {
            return [];
        }

        var ids = users.ToList();
        return (await db.EmailOptOuts
                .Where(optOut => optOut.Kind == kind.Key && ids.Contains(optOut.UserId))
                .Select(optOut => optOut.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    public static void Deliver(NotificationsDbContext db, InAppNotification notification, NotificationKind kind, bool optedOut)
    {
        db.Notifications.Add(notification);
        if (!kind.Email || optedOut)
        {
            return;
        }

        if (kind.Delivery == NotificationDelivery.DailyDigest)
        {
            notification.AwaitsDigest = true;
            return;
        }

        db.OutboundEmails.Add(new OutboundEmail
        {
            Type = EmailType.Notification,
            UserId = notification.UserId,
            Notification = notification,
            Status = EmailStatus.Pending,
            NextAttemptAt = notification.CreatedAt,
            CreatedAt = notification.CreatedAt,
        });
    }

    public static LiveEvent Created(string kind, IEnumerable<Guid> users) =>
        new("notification.created", new { kind }, LiveAudience.Users([.. users.Distinct()]));

    public static LiveEvent Read(IEnumerable<Guid> users) =>
        new("notifications.read", new { }, LiveAudience.Users([.. users.Distinct()]));

    public static string Clip(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
