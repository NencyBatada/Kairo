namespace Kairo.Contracts.Measurement;

public interface IImpactReports
{
    Task<IReadOnlyList<ImpactSummary>> ListReadyAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<ImpactSummary?> GetAsync(Guid roomId, CancellationToken cancellationToken);
}

public static class ImpactOutcomes
{
    public const string Worked = "worked";
    public const string NoEffect = "no_effect";
    public const string Inconclusive = "inconclusive";
}

public static class ImpactNextSteps
{
    public const string ScaleUp = "scale_up";
    public const string Change = "change";
    public const string Stop = "stop";
    public const string Retest = "retest";
}

public sealed record ImpactRegion(
    string Id,
    string Name,
    bool Test,
    string Status,
    bool Conclusive,
    double? Lift,
    double? Low,
    double? High,
    double? Extra,
    double? Cost);

public sealed record ImpactFlag(string Code, string Detail);

public sealed record ImpactSummary(
    Guid ReportId,
    Guid BrandId,
    Guid RoomId,
    string Title,
    string Kpi,
    string Currency,
    DateOnly StartDate,
    DateOnly ThroughDate,
    string Outcome,
    string NextStep,
    double? Lift,
    double? Low,
    double? High,
    double? Extra,
    double? ExtraLow,
    double? ExtraHigh,
    decimal Spend,
    decimal Results,
    decimal? CostPerExtra,
    string Summary,
    IReadOnlyList<ImpactFlag> Flags,
    IReadOnlyList<ImpactRegion> Regions,
    DateTimeOffset ReadyAt);
