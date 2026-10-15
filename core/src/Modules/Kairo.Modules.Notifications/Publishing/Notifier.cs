using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;
using Kairo.Modules.Notifications.Scheduling;

namespace Kairo.Modules.Notifications.Publishing;

internal sealed class Notifier(
    NotificationsDbContext db,
    KairoDatabase database,
    IBrandMembers members,
    IBrandDirectory brands,
    ILiveUpdates live,
    TimeProvider time) : INotifications
{
    public const int SubjectLength = 200;

    public async Task NotifyAsync(Notification notification, CancellationToken cancellationToken)
    {
        var brandId = database.BrandId ?? throw new InvalidOperationException("Notifying needs a workspace.");
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

        var now = time.GetUtcNow();
        if (kind.Delivery == NotificationDelivery.GroupedHourly)
        {
            foreach (var userId in recipients)
            {
                db.HeldNotifications.Add(new HeldNotification
                {
                    UserId = userId,
                    Kind = kind.Key,
                    Title = notification.Title,
                    Body = notification.Body,
                    Link = notification.Link,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var optedOut = await Inboxes.OptedOutAsync(db, kind, recipients, cancellationToken);
        DateTimeOffset? remindAt = kind.RemindAfter is { } wait
            ? WorkingHours.Add(now, wait, await ZoneAsync(brandId, cancellationToken))
            : null;
        foreach (var userId in recipients)
        {
            Inboxes.Deliver(
                db,
                new InAppNotification
                {
                    UserId = userId,
                    Kind = kind.Key,
                    Title = notification.Title,
                    Body = notification.Body,
                    Link = notification.Link,
                    Subject = notification.Subject,
                    CreatedAt = now,
                    RemindAt = remindAt,
                },
                kind,
                optedOut.Contains(userId));
        }

        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(Inboxes.Created(kind.Key, recipients), cancellationToken);
    }

    public async Task ResolveAsync(string subject, CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Resolving notifications needs a workspace.");
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectLength)
        {
            throw new ArgumentException($"A notification subject is up to {SubjectLength} characters.", nameof(subject));
        }

        var unresolved = await db.Notifications
            .Where(notification => notification.Subject == subject && notification.ResolvedAt == null)
            .ToListAsync(cancellationToken);
        if (unresolved.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var notification in unresolved)
        {
            notification.ResolvedAt = now;
            notification.RemindAt = null;
            notification.ReadAt ??= now;
        }

        var ids = unresolved.Select(notification => (long?)notification.Id).ToList();
        await db.OutboundEmails
            .Where(email => ids.Contains(email.NotificationId)
                && email.Status == EmailStatus.Pending
                && (email.ClaimedUntil == null || email.ClaimedUntil < now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.Status, EmailStatus.Skipped), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(Inboxes.Read(unresolved.Select(notification => notification.UserId)), cancellationToken);
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid brandId, CancellationToken cancellationToken) =>
        WorkingHours.ZoneOf((await brands.GetAsync([brandId], cancellationToken)).SingleOrDefault()?.TimeZone);

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

        if (notification.Subject is { } subject && (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectLength))
        {
            throw new ArgumentException($"A notification's subject is up to {SubjectLength} characters.", nameof(notification));
        }

        if (kind.RemindAfter is not null && notification.Subject is null)
        {
            throw new ArgumentException($"{kind.Name} notifications need a subject, so they can be resolved and stop reminding.", nameof(notification));
        }

        if (kind.Delivery == NotificationDelivery.GroupedHourly && notification.Subject is not null)
        {
            throw new ArgumentException($"{kind.Name} notifications are grouped hourly, so they can't have a subject.", nameof(notification));
        }

        return kind;
    }
}
