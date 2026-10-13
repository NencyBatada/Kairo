using System.Threading.Channels;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveSubscription(Guid brandId, TimeProvider time, LiveFeed feed) : IDisposable
{
    private readonly DateTimeOffset _oldestWanted = time.GetUtcNow() - LiveTimings.GapPatience;

    private readonly Channel<OutboxEvent> _events = Channel.CreateUnbounded<OutboxEvent>(new UnboundedChannelOptions { SingleReader = true });

    public Guid BrandId => brandId;

    public void Offer(OutboxEvent row)
    {
        if (row.CreatedAt >= _oldestWanted)
        {
            _events.Writer.TryWrite(row);
        }
    }

    public async Task<OutboxEvent?> NextAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (_events.Reader.TryRead(out var row))
        {
            return row;
        }

        if (wait <= TimeSpan.Zero)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(wait, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _events.Reader.WaitToReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return _events.Reader.TryRead(out row) ? row : null;
    }

    public void Dispose()
    {
        _events.Writer.TryComplete();
        feed.Unsubscribe(this);
    }
}
