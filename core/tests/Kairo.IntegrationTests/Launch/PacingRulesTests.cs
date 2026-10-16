using Kairo.Contracts.Launch;
using Kairo.Modules.Launch;

namespace Kairo.IntegrationTests.Launch;

public sealed class PacingRulesTests
{
    private static readonly LaunchPlan Plan = new(Guid.NewGuid(), Guid.NewGuid(), "Monsoon boots", 1, 1, 30_000m, "INR", "Asia/Kolkata",
        new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30), ["en"], [], [], "1111111111", null, null, [], [],
        new LaunchSettings([new LaunchChannel("search", 15_000m, "target_cpa", 300m), new LaunchChannel("demand_gen", 15_000m, "maximize_clicks")]), []);

    [Theory]
    [InlineData(500, 0)]
    [InlineData(600, 0.2)]
    [InlineData(399, -0.202)]
    [InlineData(0, -1)]
    public void Deviation_compares_spend_with_the_daily_plan(decimal spend, decimal expected) =>
        Assert.Equal(expected, PacingRules.Deviation(spend, PacingRules.PlannedDaily(Plan.Settings.Channels[0], Plan)));

    [Theory]
    [InlineData(0.2, "on_pace")]
    [InlineData(0.2001, "over")]
    [InlineData(-0.2, "on_pace")]
    [InlineData(-0.2001, "under")]
    public void More_than_twenty_percent_either_way_is_off_pace(decimal deviation, string status) =>
        Assert.Equal(status, PacingRules.Status(deviation));

    [Fact]
    public void Deviation_is_bounded_when_the_plan_is_tiny() =>
        Assert.Equal(9999m, PacingRules.Deviation(1_000_000m, 0.0001m));

    [Theory]
    [InlineData("2026-10-05T10:00:00+05:30", "2026-10-05T10:00:00+05:30", null, null)]
    [InlineData("2026-10-05T10:00:00+05:30", "2026-10-06T07:00:00+05:30", null, null)]
    [InlineData("2026-10-05T10:00:00+05:30", "2026-10-07T05:59:00+05:30", null, null)]
    [InlineData("2026-10-05T10:00:00+05:30", "2026-10-07T06:00:00+05:30", "2026-10-06", "2026-10-06")]
    [InlineData("2026-09-20T10:00:00+05:30", "2026-10-02T06:30:00+05:30", "2026-10-01", "2026-10-01")]
    [InlineData("2026-09-20T10:00:00+05:30", "2026-10-20T06:30:00+05:30", "2026-10-13", "2026-10-19")]
    [InlineData("2026-09-20T10:00:00+05:30", "2026-11-03T06:30:00+05:30", "2026-10-24", "2026-10-30")]
    public void The_window_skips_launch_day_waits_for_settled_data_and_catches_up_a_week(string activated, string now, string? first, string? last)
    {
        var window = PacingRules.Window(Plan, DateTimeOffset.Parse(activated, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(first is null ? null : new PacingWindow(DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(last!, System.Globalization.CultureInfo.InvariantCulture)), window);
    }

    [Fact]
    public void Under_pace_causes_follow_a_fixed_order()
    {
        Assert.Equal("campaign_stopped", Cause(PacingStates.Under, reasons: ["CAMPAIGN_PAUSED"], blocked: 4).Kind);
        Assert.Equal("ads_unavailable", Cause(PacingStates.Under, blocked: 1, reasons: ["BIDDING_STRATEGY_LIMITED"]).Kind);
        Assert.Equal("ads_unavailable", Cause(PacingStates.Under, reasons: ["HAS_ADS_DISAPPROVED"]).Kind);
        Assert.Equal("target_too_strict", Cause(PacingStates.Under, reasons: ["BIDDING_STRATEGY_CONSTRAINED"]).Kind);
        var strict = Cause(PacingStates.Under, rank: 0.45m);
        Assert.Equal("target_too_strict", strict.Kind);
        Assert.Contains("missed 45% of eligible searches", strict.Text, StringComparison.Ordinal);
        Assert.Equal("learning", Cause(PacingStates.Under, reasons: ["BIDDING_STRATEGY_LEARNING"], rank: 0.1m).Kind);
        Assert.Equal("low_demand", Cause(PacingStates.Under, reasons: ["SEARCH_VOLUME_LIMITED"]).Kind);
    }

    [Fact]
    public void Over_pace_names_costlier_clicks_or_higher_demand()
    {
        var costlier = Cause(PacingStates.Over, recent: 12.5m, earlier: 10m);
        Assert.Equal("costlier_clicks", costlier.Kind);
        Assert.Contains("25% more", costlier.Text, StringComparison.Ordinal);
        Assert.Equal("high_demand", Cause(PacingStates.Over, recent: 11m, earlier: 10m).Kind);
        Assert.Equal("high_demand", Cause(PacingStates.Over, recent: 11m).Kind);
        Assert.All(new[] { costlier, Cause(PacingStates.Under) }, cause => Assert.NotEmpty(cause.Fix));
    }

    private static PacingCause Cause(string status, string[]? reasons = null, int blocked = 0, decimal? rank = null, decimal? recent = null, decimal? earlier = null) =>
        PacingRules.Cause(status, new PacingSignals(reasons ?? [], 4, blocked, rank, recent, earlier));
}
