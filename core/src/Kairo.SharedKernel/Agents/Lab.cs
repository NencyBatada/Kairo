namespace Kairo.SharedKernel.Agents;

public sealed record LabSeries(string Key, string Name, double[] Values);

public sealed record LabCheckRequest(
    int Days,
    int Start,
    int ChangeDays,
    int PlannedDays,
    IReadOnlyList<LabSeries> Series,
    IReadOnlyList<string> Affected,
    IReadOnlyList<string>? Comparison);

public sealed record LabCandidate(string Key, double? Fit, bool Suggested);

public sealed record LabWarning(string Code, double? Value);

public sealed record LabCheckAnswer(
    IReadOnlyList<LabCandidate> Candidates,
    IReadOnlyList<string> Comparison,
    double? Fit,
    double? Detectable,
    int Placebos,
    IReadOnlyList<LabWarning> Warnings);

public sealed record LabRunRequest(
    int Days,
    int Start,
    int ChangeDays,
    int PlannedDays,
    IReadOnlyList<LabSeries> Series,
    IReadOnlyList<string> Affected,
    IReadOnlyList<string> Comparison,
    string Name,
    string WhatChanged,
    string Metric,
    string Source,
    string Currency);

public sealed record LabDay(int Offset, double Actual, double Expected);

public sealed record LabRunAnswer(
    string Status,
    string? Reason,
    string Outcome,
    LiftFigures? Figures,
    double? Fit,
    double? Detectable,
    int Placebos,
    IReadOnlyList<LabWarning> Warnings,
    IReadOnlyList<LabDay> Daily,
    string Summary,
    string Explanation,
    string? InconclusiveReason,
    string? NextAdvice,
    string Method,
    string? Model,
    string? PromptVersion);

public sealed class ImpactLabAgent(AgentClient agents)
{
    public const string CheckRoute = "prove/lab/check";
    public const string RunRoute = "prove/lab/run";

    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(120);

    public Task<LabCheckAnswer> CheckAsync(LabCheckRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<LabCheckRequest, LabCheckAnswer>(CheckRoute, request, CheckTimeout, cancellationToken);

    public Task<LabRunAnswer> RunAsync(LabRunRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<LabRunRequest, LabRunAnswer>(RunRoute, request, RunTimeout, cancellationToken);
}
