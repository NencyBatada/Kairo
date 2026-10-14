using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;
using Kairo.Modules.Notifications.Publishing;

namespace Kairo.Modules.Notifications.Scheduling;

internal sealed class BrandDeliveries(NotificationsDbContext db, KairoDatabase database, ILiveUpdates live)
{
    public const string ReminderPrefix = "Reminder: ";

    private readonly Dictionary<string, HashSet<Guid>> _created = [];

    public async Task<DueCounts> RunAsync(BrandSummary brand, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await database.UseBrandAsync(brand.Id, cancellationToken);
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"notifications.due.{brand.Id:N}", cancellationToken);
            var reminders = await RemindAsync(now, cancellationToken);
            var groups = await GroupAsync(now, cancellationToken);
            var digests = await DigestAsync(WorkingHours.ZoneOf(brand.TimeZone), now, cancellationToken);
            foreach (var (kind, users) in _created)
            {
                await live.PublishAsync(Inboxes.Created(kind, users), cancellationToken);
            }

            await database.CommitAsync(cancellationToken);
            return new DueCounts(reminders, groups, digests);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<int> RemindAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.Notifications
            .Where(notification => notification.RemindAt != null && notification.RemindAt <= now && notification.ResolvedAt == null)
            .OrderBy(notification => notification.Id)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var byKind in due.GroupBy(notification => notification.Kind))
        {
            var kind = NotificationKinds.Find(byKind.Key);
            var optedOut = kind is null ? [] : await Inboxes.OptedOutAsync(db, kind, [.. byKind.Select(row => row.UserId)], cancellationToken);
            foreach (var original in byKind)
            {
                original.RemindAt = null;
                if (kind is null)
                {
                    continue;
                }

                Deliver(
                    new InAppNotification
                    {
                        UserId = original.UserId,
                        Kind = original.Kind,
                        Title = Inboxes.Clip(ReminderPrefix + original.Title, 200),
                        Body = original.Body,
                        Link = original.Link,
                        Subject = original.Subject,
                        CreatedAt = now,
                    },
                    kind,
                    optedOut.Contains(original.UserId));
                sent++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }

    private async Task<int> GroupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = HourlyGroups.Cutoff(now);
        var held = await db.HeldNotifications
            .Where(item => item.CreatedAt < cutoff)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var groups = 0;
        foreach (var byKind in held.GroupBy(item => item.Kind))
        {
            var kind = NotificationKinds.Find(byKind.Key);
            if (kind is null)
            {
                continue;
            }

            var optedOut = await Inboxes.OptedOutAsync(db, kind, [.. byKind.Select(item => item.UserId).Distinct()], cancellationToken);
            foreach (var items in byKind.GroupBy(item => item.UserId).Select(group => group.ToList()))
            {
                Deliver(Combine(kind, items, now), kind, optedOut.Contains(items[0].UserId));
                groups++;
            }
        }

        db.HeldNotifications.RemoveRange(held);
        await db.SaveChangesAsync(cancellationToken);
        return groups;
    }

    private async Task<int> DigestAsync(TimeZoneInfo zone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (DailyDigest.TodaysSendTime(now, zone) is not { } sendTime)
        {
            return 0;
        }

        var waiting = await db.Notifications
            .Where(notification => notification.AwaitsDigest)
            .OrderBy(notification => notification.Id)
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return 0;
        }

        var users = waiting.Select(notification => notification.UserId).Distinct().ToList();
        var recipients = users.Select(user => (Guid?)user).ToList();
        var sentToday = (await db.OutboundEmails
                .Where(email => email.Type == EmailType.Digest && email.CreatedAt >= sendTime && recipients.Contains(email.UserId))
                .Select(email => email.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var optOuts = (await db.EmailOptOuts
                .Where(optOut => users.Contains(optOut.UserId))
                .Select(optOut => new { optOut.UserId, optOut.Kind })
                .ToListAsync(cancellationToken))
            .Select(optOut => (optOut.UserId, optOut.Kind))
            .ToHashSet();

        var digests = new List<(OutboundEmail Email, List<InAppNotification> Items)>();
        foreach (var mine in waiting.Where(notification => !sentToday.Contains(notification.UserId)).GroupBy(notification => notification.UserId))
        {
            var items = new List<InAppNotification>();
            foreach (var notification in mine)
            {
                notification.AwaitsDigest = false;
                var kind = NotificationKinds.Find(notification.Kind);
                if (kind is { Email: true } && !(kind.CanTurnOff && optOuts.Contains((mine.Key, kind.Key))))
                {
                    items.Add(notification);
                }
            }

            if (items.Count == 0)
            {
                continue;
            }

            var email = new OutboundEmail
            {
                Type = EmailType.Digest,
                UserId = mine.Key,
                Status = EmailStatus.Pending,
                NextAttemptAt = now,
                CreatedAt = now,
            };
            db.OutboundEmails.Add(email);
            digests.Add((email, items));
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var (email, items) in digests)
        {
            items.ForEach(item => item.DigestEmailId = email.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return digests.Count;
    }

    private void Deliver(InAppNotification notification, NotificationKind kind, bool optedOut)
    {
        Inboxes.Deliver(db, notification, kind, optedOut);
        if (!_created.TryGetValue(kind.Key, out var users))
        {
            _created[kind.Key] = users = [];
        }

        users.Add(notification.UserId);
    }

    private static InAppNotification Combine(NotificationKind kind, List<HeldNotification> items, DateTimeOffset now)
    {
        var first = items[0];
        if (items.Count == 1)
        {
            return new InAppNotification
            {
                UserId = first.UserId,
                Kind = kind.Key,
                Title = first.Title,
                Body = first.Body,
                Link = first.Link,
                CreatedAt = now,
            };
        }

        var lines = items.Take(HourlyGroups.ListedItems).Select(item => item.Title).ToList();
        if (items.Count > HourlyGroups.ListedItems)
        {
            lines.Add($"And {items.Count - HourlyGroups.ListedItems} more.");
        }

        return new InAppNotification
        {
            UserId = first.UserId,
            Kind = kind.Key,
            Title = Inboxes.Clip($"{kind.Name}: {items.Count} updates", 200),
            Body = Inboxes.Clip(string.Join("\n", lines), 1000),
            Link = items.All(item => item.Link == first.Link) ? first.Link : kind.GroupLink ?? first.Link,
            CreatedAt = now,
        };
    }
}

internal sealed record DueCounts(int Reminders, int Groups, int Digests);
