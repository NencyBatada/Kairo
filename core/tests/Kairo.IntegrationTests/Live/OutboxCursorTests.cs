using Kairo.Modules.Live.Delivery;

namespace Kairo.IntegrationTests.Live;

public sealed class OutboxCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_cursor_starts_after_the_newest_event()
    {
        var cursor = OutboxCursor.Start(41, [], Now);

        Assert.Equal(41, cursor.Floor);
    }

    [Fact]
    public void New_events_are_accepted_once_in_order()
    {
        var cursor = OutboxCursor.Start(10, [], Now);

        Assert.Equal([11L, 12L], cursor.Advance([11, 12], Now));
        Assert.Empty(cursor.Advance([11, 12], Now));
        Assert.Equal(12, cursor.Floor);
    }

    [Fact]
    public void An_event_that_commits_after_a_later_one_is_still_accepted()
    {
        var cursor = OutboxCursor.Start(10, [], Now);

        Assert.Equal([12L], cursor.Advance([12], Now));
        Assert.Equal(10, cursor.Floor);
        Assert.Equal([11L], cursor.Advance([11, 12], Now.AddSeconds(5)));
        Assert.Equal(12, cursor.Floor);
    }

    [Fact]
    public void Filling_part_of_a_gap_keeps_waiting_for_the_rest()
    {
        var cursor = OutboxCursor.Start(10, [], Now);
        cursor.Advance([14], Now);

        Assert.Equal([12L], cursor.Advance([12, 14], Now));
        Assert.Equal(10, cursor.Floor);
        Assert.Equal([11L, 13L], cursor.Advance([11, 12, 13, 14], Now));
        Assert.Equal(14, cursor.Floor);
    }

    [Fact]
    public void A_gap_that_never_fills_is_given_up_after_a_minute()
    {
        var cursor = OutboxCursor.Start(10, [], Now);
        cursor.Advance([13], Now);

        cursor.Advance([13], Now + LiveTimings.GapPatience - TimeSpan.FromSeconds(1));
        Assert.Equal(10, cursor.Floor);
        cursor.Advance([13], Now + LiveTimings.GapPatience);
        Assert.Equal(13, cursor.Floor);
        Assert.Empty(cursor.Advance([11], Now + LiveTimings.GapPatience));
    }

    [Fact]
    public void Starting_treats_recent_events_as_seen_and_waits_for_their_gaps()
    {
        var cursor = OutboxCursor.Start(20, [18, 20], Now);

        Assert.Equal(18, cursor.Floor);
        Assert.Equal([19L], cursor.Advance([18, 19, 20], Now));
        Assert.Equal(20, cursor.Floor);
    }
}
