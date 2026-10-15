namespace Kairo.SharedKernel.Agents;

public sealed record ProofRegion(
    string Id,
    bool InArea,
    double[]? Orders,
    double[]? Conversions,
    double[]? Revenue,
    double[]? Searches);

public sealed record ProofPlanRequest(
    int Days,
    int? MaxDays,
    double TargetLift,
    IReadOnlyList<string>? HoldoutIds,
    IReadOnlyList<ProofRegion> Regions);

public sealed record ProofCandidate(string Id, double Score, double Share, bool InArea);

public sealed record ProofLift(int Days, double? Lift, int Placebos);

public sealed record ProofWarning(string Code, string Message);

public sealed record ProofPlanAnswer(
    string Kpi,
    IReadOnlyList<string> TestIds,
    IReadOnlyList<string> HoldoutIds,
    IReadOnlyList<ProofCandidate> Candidates,
    bool InsideArea,
    int? WindowDays,
    IReadOnlyList<ProofLift> Lifts,
    double? MinDetectableLift,
    int PlaceboWindows,
    double? HoldoutShare,
    double? HoldoutDailyRevenue,
    double? HoldoutCost,
    int DataDays,
    IReadOnlyList<ProofWarning> Warnings,
    string Method);

public sealed class ProofPlanAgent(AgentClient agents)
{
    public const string Route = "plan/proof-plan";
    public const double TargetLift = 0.10;
    public const int MaxRegions = 120;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public Task<ProofPlanAnswer> DesignAsync(ProofPlanRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<ProofPlanRequest, ProofPlanAnswer>(Route, request, Timeout, cancellationToken);
}
