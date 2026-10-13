using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Feeds;

public sealed class FeedClockTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("Asia/Kolkata", "2026-10-14")]
    [InlineData("America/Los_Angeles", "2026-10-13")]
    [InlineData("Mars/Olympus", "2026-10-14")]
    [InlineData("", "2026-10-14")]
    [InlineData(null, "2026-10-14")]
    public void Today_is_the_date_in_the_time_zone(string? timeZone, string expected)
    {
        Assert.Equal(expected, FeedClock.Iso(FeedClock.Today(_time, timeZone)));
    }

    [Fact]
    public void A_window_covers_the_7_days_through_yesterday()
    {
        var window = FeedWindow.For(new DateOnly(2026, 10, 14), firstRun: false);

        Assert.Equal(new FeedWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)), window);
        Assert.Equal(7, window.Days().Count);
        Assert.Equal(new DateOnly(2026, 10, 13), window.Days()[^1]);
    }

    [Fact]
    public void A_first_window_reaches_back_13_months()
    {
        var window = FeedWindow.For(new DateOnly(2026, 10, 14), firstRun: true);

        Assert.Equal(new DateOnly(2025, 9, 14), window.From);
        Assert.Equal(new DateOnly(2026, 10, 13), window.To);
        Assert.Equal(395, window.Days().Count);
    }
}
