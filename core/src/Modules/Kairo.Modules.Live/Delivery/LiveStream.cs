using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, TimeProvider time)
{
    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat();
        var lastWrite = time.GetUtcNow();
        var sent = new HashSet<long>();

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
}
