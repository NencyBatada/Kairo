using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.Publishing;

internal sealed class Notifier(
    NotificationsDbContext db,
    KairoDatabase database,
    IBrandMembers members,
    ILiveUpdates live,
    TimeProvider time) : INotifications
{
    public async Task NotifyAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Notifying needs a workspace.");
        }

        var kind = Validate(notification);
        var recipients = (await members.ListAsync(cancellationToken))
            .Where(member => notification.Audience.Includes(member.UserId, member.Roles))
            .Select(member => member.UserId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        List<Guid> optedOut = kind.CanTurnOff
            ? await db.EmailOptOuts
                .Where(optOut => optOut.Kind == kind.Key && recipients.Contains(optOut.UserId))
                .Select(optOut => optOut.UserId)
                .ToListAsync(cancellationToken)
            : [];

        var now = time.GetUtcNow();
        foreach (var userId in recipients)
        {
            var row = new InAppNotification
            {
                UserId = userId,
                Kind = kind.Key,
                Title = notification.Title,
                Body = notification.Body,
                Link = notification.Link,
                CreatedAt = now,
            };
            db.Notifications.Add(row);
            if (kind.Email && !optedOut.Contains(userId))
            {
                db.OutboundEmails.Add(new OutboundEmail
                {
                    UserId = userId,
                    Notification = row,
                    Status = EmailStatus.Pending,
                    NextAttemptAt = now,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(
            new LiveEvent("notification.created", new { kind = kind.Key }, LiveAudience.Users([.. recipients])),
            cancellationToken);
    }

    private static NotificationKind Validate(Notification notification)
    {
        var kind = NotificationKinds.Find(notification.Kind)
            ?? throw new ArgumentException($"'{notification.Kind}' isn't a notification kind.", nameof(notification));
        if (string.IsNullOrWhiteSpace(notification.Title) || notification.Title.Length > 200)
        {
            throw new ArgumentException("A notification needs a title of up to 200 characters.", nameof(notification));
        }

        if (notification.Body.Length > 1000)
        {
            throw new ArgumentException("A notification's body can be up to 1,000 characters.", nameof(notification));
        }

        if (!notification.Link.StartsWith('/') || notification.Link.Length > 400)
        {
            throw new ArgumentException("A notification's link is a path inside the workspace, starting with '/', of up to 400 characters.", nameof(notification));
        }

        return kind;
    }
}
