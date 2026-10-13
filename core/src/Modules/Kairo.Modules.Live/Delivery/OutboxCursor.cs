namespace Kairo.Modules.Live.Delivery;

internal sealed class OutboxCursor
{
    private readonly List<Gap> _gaps = [];
    private long _highest;

    private OutboxCursor(long highest) => _highest = highest;

    public long Floor => _gaps.Count == 0 ? _highest : _gaps.Min(gap => gap.From) - 1;

    public static OutboxCursor Start(long highest, IReadOnlyList<long> recent, DateTimeOffset now)
    {
        var cursor = new OutboxCursor(recent.Count == 0 ? highest : recent.Min() - 1);
        cursor.Advance(recent, now);
        return cursor;
    }

    public IReadOnlyList<long> Advance(IReadOnlyList<long> ids, DateTimeOffset now)
    {
        var accepted = new List<long>();
        foreach (var id in ids.Order())
        {
            if (id > _highest)
            {
                if (id > _highest + 1)
                {
                    _gaps.Add(new Gap(_highest + 1, id - 1, now));
                }

                _highest = id;
                accepted.Add(id);
            }
            else if (FillGap(id))
            {
                accepted.Add(id);
            }
        }

        _gaps.RemoveAll(gap => now - gap.OpenedAt >= LiveTimings.GapPatience);
        return accepted;
    }

    private bool FillGap(long id)
    {
        var index = _gaps.FindIndex(gap => gap.From <= id && id <= gap.To);
        if (index < 0)
        {
            return false;
        }

        var gap = _gaps[index];
        _gaps.RemoveAt(index);
        if (gap.From < id)
        {
            _gaps.Add(gap with { To = id - 1 });
        }

        if (id < gap.To)
        {
            _gaps.Add(gap with { From = id + 1 });
        }

        return true;
    }

    private sealed record Gap(long From, long To, DateTimeOffset OpenedAt);
}
