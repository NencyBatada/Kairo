namespace Kairo.SharedKernel.Agents;

public sealed record ReportRegion(string Name, bool Test, double? Lift, double? Low, double? High, bool Conclusive);

public sealed record ReportFlag(string Code, string Detail);

public sealed record ReportNextTest(int? Days, bool AddRegions, IReadOnlyList<string> Reasons);

public sealed record ReportRequest(
    string Title,
    string Kpi,
    string Currency,
    int Days,
    int PlannedDays,
    string Outcome,
    string NextStep,
    string? Reason,
    double? Lift,
    double? Low,
    double? High,
    double? Actual,
    double? Expected,
    double? Extra,
    double? ExtraLow,
    double? ExtraHigh,
    double? RevenueLift,
    double? RevenueExtra,
    decimal Spend,
    decimal Results,
    decimal? CostPerResult,
    decimal? CostPerExtra,
    decimal? CostPerExtraLow,
    decimal? CostPerExtraHigh,
    decimal? TargetCost,
    IReadOnlyList<ReportFlag> Flags,
    ReportNextTest? NextTest,
    IReadOnlyList<ReportRegion> Regions);

public sealed record ReportAnswer(
    string Summary,
    string NextStepReasoning,
    string? InconclusiveReason,
    string? NextTestAdvice,
    string Model,
    string PromptVersion);

public sealed class ReportAgent(AgentClient agents)
{
    public const string Route = "prove/report";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public Task<ReportAnswer> WriteAsync(ReportRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<ReportRequest, ReportAnswer>(Route, request, Timeout, cancellationToken);
}
