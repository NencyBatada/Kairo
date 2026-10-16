using Kairo.Contracts.Measurement;

namespace Kairo.Modules.Measurement.Impact;

internal sealed record ImpactInput(
    bool Measured,
    bool Conclusive,
    double? Lift,
    double? Low,
    double? High,
    double? Extra,
    double? ExtraLow,
    double? ExtraHigh,
    double? Spread,
    int Days,
    int PlannedDays,
    double? MinDetectableLift,
    decimal Spend,
    decimal Results,
    decimal? TargetCost);

internal sealed record NextTestPlan(int? Days, bool AddRegions, string[] Reasons);

internal sealed record ImpactDecision(
    string Outcome,
    string NextStep,
    decimal? CostPerResult,
    decimal? CostPerExtra,
    decimal? CostPerExtraLow,
    decimal? CostPerExtraHigh,
    NextTestPlan? NextTest);

internal static class ImpactRules
{
    public const double RangeZ = 1.645;
    public const int MaxTestDays = 56;
    public const int MinTestDays = 14;
    public const double SmallestReadableLift = 0.005;

    public static ImpactDecision Decide(ImpactInput input)
    {
        var outcome = Outcome(input);
        var (perExtra, perExtraLow, perExtraHigh) = CostPerExtra(input.Spend, input.Extra, input.ExtraLow, input.ExtraHigh);
        var nextStep = outcome switch
        {
            ImpactOutcomes.Worked when input.TargetCost is { } target && perExtra is { } cost && cost > target => ImpactNextSteps.Change,
            ImpactOutcomes.Worked => ImpactNextSteps.ScaleUp,
            ImpactOutcomes.NoEffect => ImpactNextSteps.Stop,
            _ => ImpactNextSteps.Retest,
        };
        return new ImpactDecision(outcome, nextStep, input.Results > 0 ? Round(input.Spend / input.Results) : null, perExtra, perExtraLow, perExtraHigh,
            outcome == ImpactOutcomes.Inconclusive ? NextTest(input) : null);
    }

    public static string Outcome(ImpactInput input)
    {
        if (!input.Measured || input.Lift is null || input.Low is not { } low || input.High is not { } high) return ImpactOutcomes.Inconclusive;
        if (input.Conclusive && low > 0) return ImpactOutcomes.Worked;
        if (input.Conclusive && high < 0) return ImpactOutcomes.NoEffect;
        if (input.MinDetectableLift is { } smallest && smallest > 0 && high < smallest && low > -smallest) return ImpactOutcomes.NoEffect;
        return ImpactOutcomes.Inconclusive;
    }

    public static (decimal? Cost, decimal? Low, decimal? High) CostPerExtra(decimal spend, double? extra, double? extraLow, double? extraHigh)
    {
        if (extra is not { } value || value <= 0) return (null, null, null);
        decimal? Per(double? count) => count is { } amount && amount > 0 ? Round(spend / (decimal)amount) : null;
        return (Per(value), Per(extraHigh), Per(extraLow));
    }

    public static NextTestPlan NextTest(ImpactInput input)
    {
        var reasons = new List<string>();
        if (!input.Measured) reasons.Add("thin_data");
        else reasons.Add("wide_range");
        if (input.Days < input.PlannedDays) reasons.Add("short_window");
        var days = Math.Max(input.Days, 1);
        var needed = (double)Math.Max(MinTestDays, Weeks(days * 2));
        if (input.Measured && input.Spread is { } spread && spread > 0 && input.Lift is { } lift && Math.Abs(Math.Log(1 + lift)) >= SmallestReadableLift)
        {
            var ratio = RangeZ * spread / Math.Abs(Math.Log(1 + lift));
            needed = Math.Max(MinTestDays, Math.Max(Weeks(days + 7), Weeks((int)Math.Ceiling(Math.Min(days * ratio * ratio, MaxTestDays * 4)))));
        }

        var addRegions = needed > MaxTestDays || !input.Measured;
        if (addRegions) reasons.Add("few_regions");
        return new NextTestPlan((int)Math.Min(needed, MaxTestDays), addRegions, [.. reasons]);
    }

    private static int Weeks(int days) => (int)Math.Ceiling(days / 7.0) * 7;

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
