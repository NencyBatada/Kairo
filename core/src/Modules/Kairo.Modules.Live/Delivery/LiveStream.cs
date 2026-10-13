using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, LiveDbContext db, TimeProvider time)
{
    private const int ReplayLimit = 1000;

    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        long? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat();
        var sent = new HashSet<long>();
        if (lastEventId is { } after)
        {
            var missed = await ReplayAsync(after, cancellationToken);
            if (missed is null)
            {
                yield return LiveItems.Reset();
            }
            else
            {
                foreach (var row in missed)
                {
                    if (viewer.CanSee(row) && sent.Add(row.Id))
                    {
                        yield return LiveItems.From(row);
                    }
                }
            }
        }

        var lastWrite = time.GetUtcNow();
        while (true)
        {
            var now = time.GetUtcNow();
            if (now - lastWrite >= LiveTimings.HeartbeatInterval)
            {
                yield return LiveItems.Heartbeat();
                lastWrite = now;
            }

            var row = await subscription.NextAsync(lastWrite + LiveTimings.HeartbeatInterval - now, cancellationToken);
            if (row is not null && viewer.CanSee(row) && sent.Add(row.Id))
            {
                yield return LiveItems.From(row);
                lastWrite = time.GetUtcNow();
            }
        }
    }

    private async Task<List<OutboxEvent>?> ReplayAsync(long after, CancellationToken cancellationToken)
    {
        var anchoredAt = await db.Unlocked()
            .Where(row => row.Id == after)
            .Select(row => (DateTimeOffset?)row.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (anchoredAt is not { } anchor || anchor < time.GetUtcNow() - LiveTimings.Retention)
        {
            return null;
        }

        var since = anchor - LiveTimings.GapPatience;
        var missed = await db.Unlocked()
            .Where(row => row.Id > after || (row.Id < after && row.CreatedAt >= since))
            .OrderBy(row => row.Id)
            .Take(ReplayLimit + 1)
            .ToListAsync(cancellationToken);
        return missed.Count > ReplayLimit ? null : missed;
    }
}
