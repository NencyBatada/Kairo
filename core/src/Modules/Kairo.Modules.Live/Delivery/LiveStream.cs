using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kairo.Contracts.Identity;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, LiveDbContext db, IBrandAccess access, ISessionDirectory sessions, TimeProvider time)
{
    private const int ReplayLimit = 1000;

    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        LivePosition? position,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opened = time.GetUtcNow();
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat(time.GetUtcNow());
        var sent = new HashSet<long>();
        if (position is not null)
        {
            var missed = position.AfterId is { } after
                ? await ReplayAsync(after, cancellationToken)
                : await ReplaySinceAsync(position.Since!.Value, cancellationToken);
            if (missed is null)
            {
                yield return LiveItems.Reset(time.GetUtcNow());
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
        var lastCheck = opened;
        while (true)
        {
            var now = time.GetUtcNow();
            if (now - opened >= LiveTimings.Lifetime)
            {
                yield break;
            }

            if (now - lastCheck >= LiveTimings.AccessCheckInterval)
            {
                if (!await StillAllowedAsync(viewer, cancellationToken))
                {
                    yield break;
                }

                lastCheck = now;
            }

            if (now - lastWrite >= LiveTimings.HeartbeatInterval)
            {
                yield return LiveItems.Heartbeat(now);
                lastWrite = now;
            }

            var wake = new[]
            {
                opened + LiveTimings.Lifetime,
                lastCheck + LiveTimings.AccessCheckInterval,
                lastWrite + LiveTimings.HeartbeatInterval,
            }.Min();
            var row = await subscription.NextAsync(wake - now, cancellationToken);
            if (row is null)
            {
                continue;
            }

            if (ConcernsViewer(row, viewer))
            {
                if (!await StillAllowedAsync(viewer, cancellationToken))
                {
                    yield break;
                }

                lastCheck = time.GetUtcNow();
            }

            if (viewer.CanSee(row) && sent.Add(row.Id))
            {
                yield return LiveItems.From(row);
                lastWrite = time.GetUtcNow();
            }
        }
    }

    private static bool ConcernsViewer(OutboxEvent row, Viewer viewer)
    {
        if (row.Type != "team.changed")
        {
            return false;
        }

        using var payload = JsonDocument.Parse(row.Payload);
        return payload.RootElement.TryGetProperty("userId", out var userId)
            && userId.TryGetGuid(out var id)
            && id == viewer.UserId;
    }

    private async Task<bool> StillAllowedAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        if (viewer.SessionKey is { } key && !await sessions.IsActiveAsync(key, cancellationToken))
        {
            return false;
        }

        if (await access.FindRolesAsync(viewer.BrandId, viewer.UserId, cancellationToken) is not { } roles)
        {
            return false;
        }

        viewer.Roles = roles;
        return true;
    }

    private async Task<List<OutboxEvent>?> ReplaySinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (since < time.GetUtcNow() - LiveTimings.Retention)
        {
            return null;
        }

        var from = since - LiveTimings.GapPatience;
        var missed = await db.Unlocked()
            .Where(row => row.CreatedAt >= from)
            .OrderBy(row => row.Id)
            .Take(ReplayLimit + 1)
            .ToListAsync(cancellationToken);
        return missed.Count > ReplayLimit ? null : missed;
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
