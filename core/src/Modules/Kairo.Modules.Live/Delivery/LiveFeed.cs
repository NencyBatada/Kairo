using System.Collections.Concurrent;
using Kairo.Modules.Live.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveFeed(IServiceScopeFactory scopes, TimeProvider time, ILogger<LiveFeed> logger) : IAsyncDisposable
{
    private const int BatchSize = 1000;

    private readonly ConcurrentDictionary<LiveSubscription, byte> _subscriptions = new();
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private OutboxCursor? _cursor;
    private Task? _loop;

    public async Task<LiveSubscription> SubscribeAsync(Guid brandId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        var subscription = new LiveSubscription(brandId, time, this);
        _subscriptions.TryAdd(subscription, 0);
        return subscription;
    }

    public void Unsubscribe(LiveSubscription subscription) => _subscriptions.TryRemove(subscription, out _);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _stopping.Dispose();
        _starting.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _loop) is not null)
        {
            return;
        }

        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (_loop is null)
            {
                _cursor = await StartCursorAsync(cancellationToken);
                Volatile.Write(ref _loop, Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None));
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    private async Task<OutboxCursor> StartCursorAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var db = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
        var now = time.GetUtcNow();
        var since = now - LiveTimings.GapPatience;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var highest = await db.Unlocked().MaxAsync(row => (long?)row.Id, cancellationToken) ?? 0;
            var recent = await db.Unlocked()
                .Where(row => row.CreatedAt >= since)
                .Select(row => row.Id)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);
            return OutboxCursor.Start(highest, recent, now);
        }
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stopping);
                var now = time.GetUtcNow();
                if (now - lastCleanup >= LiveTimings.CleanupInterval)
                {
                    await CleanUpAsync(stopping);
                    lastCleanup = now;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Reading or cleaning up live updates failed.");
            }

            try
            {
                await Task.Delay(LiveTimings.PollInterval, time, stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var cursor = _cursor!;
        var floor = cursor.Floor;
        List<OutboxEvent> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var db = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
            await using (await database.EnterSystemScopeAsync(cancellationToken))
            {
                rows = await db.Unlocked()
                    .Where(row => row.Id > floor)
                    .OrderBy(row => row.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
            }
        }

        var fresh = cursor.Advance(rows.Select(row => row.Id).ToList(), time.GetUtcNow()).ToHashSet();
        foreach (var row in rows.Where(row => fresh.Contains(row.Id)))
        {
            foreach (var subscription in _subscriptions.Keys.Where(subscription => subscription.BrandId == row.BrandId))
            {
                subscription.Offer(row);
            }
        }
    }

    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxCleanup>().RunAsync(cancellationToken);
    }
}
