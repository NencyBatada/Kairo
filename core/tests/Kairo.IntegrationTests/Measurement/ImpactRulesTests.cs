using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Impact;

namespace Kairo.IntegrationTests.Measurement;

public sealed class ImpactRulesTests
{
    private static ImpactInput Input(double lift = 0.12, double low = 0.06, double high = 0.18, bool conclusive = true, double? extra = 168,
        double? extraLow = 84, double? extraHigh = 252, double spread = 0.03, int days = 14, int planned = 14, double? smallest = 0.08,
        decimal spend = 52080, decimal results = 410, decimal? target = null, bool measured = true) =>
        new(measured, conclusive, lift, low, high, extra, extraLow, extraHigh, spread, days, planned, smallest, spend, results, target);

    [Fact]
    public void A_conclusive_lift_at_or_under_the_target_cost_scales_up()
    {
        var decision = ImpactRules.Decide(Input(target: 310));

        Assert.Equal((ImpactOutcomes.Worked, ImpactNextSteps.ScaleUp), (decision.Outcome, decision.NextStep));
        Assert.Equal((310m, 206.67m, 620m), (decision.CostPerExtra, decision.CostPerExtraLow, decision.CostPerExtraHigh));
        Assert.Equal(127.02m, decision.CostPerResult);
        Assert.Null(decision.NextTest);
    }

    [Fact]
    public void A_conclusive_lift_without_a_target_scales_up()
    {
        Assert.Equal(ImpactNextSteps.ScaleUp, ImpactRules.Decide(Input()).NextStep);
    }

    [Fact]
    public void A_conclusive_lift_that_costs_more_than_the_target_needs_changes()
    {
        var decision = ImpactRules.Decide(Input(target: 300));

        Assert.Equal((ImpactOutcomes.Worked, ImpactNextSteps.Change), (decision.Outcome, decision.NextStep));
    }

    [Fact]
    public void A_conclusive_fall_stops()
    {
        var decision = ImpactRules.Decide(Input(lift: -0.12, low: -0.18, high: -0.06, extra: -168, extraLow: -252, extraHigh: -84));

        Assert.Equal((ImpactOutcomes.NoEffect, ImpactNextSteps.Stop), (decision.Outcome, decision.NextStep));
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null), (decision.CostPerExtra, decision.CostPerExtraLow, decision.CostPerExtraHigh));
    }

    [Fact]
    public void A_range_inside_the_smallest_detectable_lift_is_no_effect()
    {
        var decision = ImpactRules.Decide(Input(lift: 0.01, low: -0.05, high: 0.07, conclusive: false, extra: 14, extraLow: -70, extraHigh: 98));

        Assert.Equal((ImpactOutcomes.NoEffect, ImpactNextSteps.Stop), (decision.Outcome, decision.NextStep));
        Assert.Equal((3720m, 531.43m, (decimal?)null), (decision.CostPerExtra, decision.CostPerExtraLow, decision.CostPerExtraHigh));
    }

    [Fact]
    public void A_wide_range_is_inconclusive_and_asks_for_a_longer_test()
    {
        var decision = ImpactRules.Decide(Input(lift: 0.03, low: -0.03, high: 0.09, conclusive: false, spread: 0.03));

        Assert.Equal((ImpactOutcomes.Inconclusive, ImpactNextSteps.Retest), (decision.Outcome, decision.NextStep));
        var next = decision.NextTest!;
        Assert.Equal((42, false), (next.Days, next.AddRegions));
        Assert.Equal(["wide_range"], next.Reasons);
    }

    [Fact]
    public void A_tiny_lift_needs_more_regions_as_well_as_more_days()
    {
        var next = ImpactRules.Decide(Input(lift: 0.01, low: -0.09, high: 0.11, conclusive: false, spread: 0.06)).NextTest!;

        Assert.Equal((ImpactRules.MaxTestDays, true), (next.Days, next.AddRegions));
        Assert.Equal(["wide_range", "few_regions"], next.Reasons);
    }

    [Fact]
    public void Thin_data_is_inconclusive_with_a_longer_test_and_more_regions()
    {
        var decision = ImpactRules.Decide(Input(measured: false, days: 10, planned: 14) with { Lift = null, Low = null, High = null, Extra = null });

        Assert.Equal((ImpactOutcomes.Inconclusive, ImpactNextSteps.Retest), (decision.Outcome, decision.NextStep));
        Assert.Null(decision.CostPerExtra);
        Assert.Equal((21, true), (decision.NextTest!.Days, decision.NextTest.AddRegions));
        Assert.Equal(["thin_data", "short_window", "few_regions"], decision.NextTest.Reasons);
    }

    [Fact]
    public void No_attributed_results_leave_the_cost_per_result_empty()
    {
        Assert.Null(ImpactRules.Decide(Input(results: 0)).CostPerResult);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void No_extra_results_leave_the_cost_per_extra_result_empty(double extra)
    {
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null), ImpactRules.CostPerExtra(1000, extra, -10, 5));
    }
}
