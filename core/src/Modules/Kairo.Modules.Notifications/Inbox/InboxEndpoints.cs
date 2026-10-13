using Kairo.Contracts.Live;
using Kairo.Modules.Notifications.Data;

namespace Kairo.Modules.Notifications.Inbox;

internal static class InboxEndpoints
{
    public static void MapInbox(this IEndpointRouteBuilder api)
    {
        var inbox = api.MapBrandApi().MapGroup("/notifications");
        inbox.MapGet("", ListAsync);
        inbox.MapPost("/{id:long}/read", ReadAsync);
        inbox.MapPost("/read-all", ReadAllAsync);
    }

    private static async Task<Ok<NotificationPage>> ListAsync(
        long? before,
        int? limit,
        CurrentMember member,
        NotificationsDbContext db,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 30, 1, 100);
        var mine = db.Notifications.AsNoTracking().Where(notification => notification.UserId == member.UserId);
        var page = before is { } cursor ? mine.Where(notification => notification.Id < cursor) : mine;
        var rows = await page
            .OrderByDescending(notification => notification.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Take(take)
            .Select(row => new NotificationItem(row.Id, row.Kind, row.Title, row.Body, row.Link, row.CreatedAt, row.ReadAt))
            .ToList();
        var unread = await mine.CountAsync(notification => notification.ReadAt == null, cancellationToken);
        return TypedResults.Ok(new NotificationPage(items, unread, rows.Count > take ? items[^1].Id : null));
    }

    private static async Task<Results<NoContent, NotFound>> ReadAsync(
        long id,
        CurrentMember member,
        NotificationsDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
            row => row.Id == id && row.UserId == member.UserId,
            cancellationToken);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(ReadEvent(member.UserId), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ReadAllAsync(
        CurrentMember member,
        NotificationsDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? now = time.GetUtcNow();
        var changed = await db.Notifications
            .Where(notification => notification.UserId == member.UserId && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAt, now), cancellationToken);
        if (changed > 0)
        {
            await live.PublishAsync(ReadEvent(member.UserId), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static LiveEvent ReadEvent(Guid userId) => new("notifications.read", new { }, LiveAudience.Users(userId));
}

internal sealed record NotificationItem(
    long Id,
    string Kind,
    string Title,
    string Body,
    string Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

internal sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int UnreadCount, long? NextBefore);
