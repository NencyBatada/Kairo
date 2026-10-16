namespace Kairo.SharedKernel.Agents;

public sealed record LiftRegion(string Id, bool Test, double[]? Outcome, double[]? Revenue);

public sealed record LiftRequest(string Kpi, int Days, int Start, int TestDays, IReadOnlyList<LiftRegion> Regions);

public sealed record LiftFigures(
    double Lift,
    double Low,
    double High,
    double Actual,
    double Expected,
    double Extra,
    double ExtraLow,
    double ExtraHigh,
    bool Conclusive,
    int Placebos,
    double Spread);

public sealed record LiftRegionAnswer(string Id, string Status, string? Reason, LiftFigures? Figures);

public sealed record LiftDay(double Actual, double Expected);

public sealed record LiftAnswer(
    string Status,
    string? Reason,
    string Kpi,
    int TestDays,
    LiftFigures? Outcome,
    LiftFigures? Revenue,
    IReadOnlyList<LiftRegionAnswer> Regions,
    IReadOnlyList<LiftDay> Daily,
    string Method);

public sealed class LiftAgent(AgentClient agents)
{
    public const string Route = "prove/lift";
    public const int HistoryDays = 364;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public Task<LiftAnswer> MeasureAsync(LiftRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<LiftRequest, LiftAnswer>(Route, request, Timeout, cancellationToken);
}
