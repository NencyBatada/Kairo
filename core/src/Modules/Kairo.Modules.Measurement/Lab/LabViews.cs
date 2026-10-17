using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Measurement.Lab;

internal sealed record LabSetupRequest(
    int? Version,
    string? Name,
    string? WhatChanged,
    DateOnly? Start,
    DateOnly? End,
    string? AffectsKind,
    LabSourceRequest? Source);

internal sealed record LabSelectionRequest(int? Version, string[]? Affected, string[]? Comparison);

internal sealed record LabVersionRequest(int? Version);

internal sealed record LabSourceView(
    string Kind,
    string Metric,
    string Grain,
    string? Table,
    string? DateColumn,
    string? ValueColumn,
    string? SeriesColumn,
    string? FileName);

internal sealed record LabSeriesView(string Key, string Name, double Total);

internal sealed record LabDataView(
    DateOnly From,
    DateOnly To,
    int HistoryDays,
    int ChangeDays,
    int PlannedDays,
    string? Currency,
    LabSeriesView[] Series);

internal sealed record LabCandidateView(string Key, string Name, double? Fit, bool Suggested);

internal sealed record LabWarningView(string Code, double? Value, string Message);

internal sealed record LabDayView(DateOnly Date, double Actual, double Expected, bool During);

internal sealed record LabResultView(
    string Status,
    string? Reason,
    string Outcome,
    bool Conclusive,
    LiftFigures? Figures,
    string? Currency,
    LabDayView[] Daily,
    string? Summary,
    string? Explanation,
    string? InconclusiveReason,
    string? NextAdvice,
    string? Method,
    string? Model,
    string? PromptVersion,
    DateTimeOffset? FinishedAt);

internal sealed record LabAnalysisView(
    Guid Id,
    int Version,
    string Name,
    string WhatChanged,
    DateOnly Start,
    DateOnly End,
    string AffectsKind,
    LabSourceView Source,
    string State,
    LabDataView? Data,
    string[] Affected,
    string[] Comparison,
    LabCandidateView[] Candidates,
    double? Fit,
    double? Detectable,
    LabWarningView[] Warnings,
    LabResultView? Result,
    string? Problem,
    Guid? RunBy,
    DateTimeOffset? RunAt,
    Guid? MemoryEntryId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record LabAnalysisSummary(
    Guid Id,
    string Name,
    string State,
    DateOnly Start,
    DateOnly End,
    string SourceKind,
    string Metric,
    string? Outcome,
    bool Conclusive,
    double? Lift,
    double? Low,
    double? High,
    Guid? MemoryEntryId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record LabAnalysisList(LabAnalysisSummary[] Analyses);

internal sealed record LabStoredResult(string Status, string? Reason, LiftFigures? Figures, IReadOnlyList<LabDay> Daily);

internal static class LabViews
{
    public const string AuditTarget = "impact_lab_analysis";

    public static LiveEvent Changed(Guid analysisId) => new("impact_lab.changed", new { analysisId }, LiveAudience.Everyone);

    public static int PlannedDays(LabAnalysis analysis) => analysis.End.DayNumber - analysis.Start.DayNumber + 1;

    public static LabAnalysisView Of(LabAnalysis analysis, LabDataset? dataset)
    {
        var spec = LabSourceSpec.Read(analysis.Source);
        var series = dataset?.Series is { } stored ? Read<LabSeriesData[]>(stored) ?? [] : [];
        var names = series.ToDictionary(item => item.Key, item => item.Name, StringComparer.Ordinal);
        var data = dataset is { From: { } from, To: { } to } && analysis.HistoryDays is { } history && analysis.ChangeDays is { } change
            ? new LabDataView(from, to, history, change, PlannedDays(analysis), dataset.Currency,
                [.. series.Select(item => new LabSeriesView(item.Key, item.Name, Math.Round(item.Values.Sum(), 4)))])
            : null;
        return new LabAnalysisView(
            analysis.Id,
            analysis.Version,
            analysis.Name,
            analysis.WhatChanged,
            analysis.Start,
            analysis.End,
            analysis.AffectsKind,
            new LabSourceView(spec.Kind, spec.Metric, spec.Grain, spec.Table, spec.DateColumn, spec.ValueColumn, spec.SeriesColumn, dataset?.FileName),
            analysis.State,
            data,
            Keys(analysis.Affected),
            Keys(analysis.Comparison),
            [.. (Read<LabCandidate[]>(analysis.Candidates) ?? []).Select(candidate =>
                new LabCandidateView(candidate.Key, names.GetValueOrDefault(candidate.Key, candidate.Key), candidate.Fit, candidate.Suggested))],
            analysis.Fit,
            analysis.Detectable,
            [.. (Read<LabWarning[]>(analysis.Warnings) ?? []).Select(warning => Warning(warning, analysis))],
            Result(analysis, dataset),
            analysis.Problem,
            analysis.RunBy,
            analysis.RunAt,
            analysis.MemoryEntryId,
            analysis.CreatedBy,
            analysis.CreatedAt,
            analysis.UpdatedAt);
    }

    public static LabAnalysisSummary Summary(LabAnalysis analysis)
    {
        var spec = LabSourceSpec.Read(analysis.Source);
        var figures = analysis.Result is { } result ? Read<LabStoredResult>(result)?.Figures : null;
        return new LabAnalysisSummary(analysis.Id, analysis.Name, analysis.State, analysis.Start, analysis.End, spec.Kind, spec.Metric, analysis.Outcome,
            analysis.Conclusive, figures?.Lift, figures?.Low, figures?.High, analysis.MemoryEntryId, analysis.CreatedBy, analysis.CreatedAt, analysis.UpdatedAt);
    }

    public static LabWarningView Warning(LabWarning warning, LabAnalysis analysis) => new(warning.Code, warning.Value, warning.Code switch
    {
        "short_history" => $"There are only {Whole(warning.Value)} days of data before the change. Kairo needs at least 56 days (8 weeks) for a reliable answer.",
        "no_comparison" => "Pick at least one comparison series that the change didn't touch.",
        "weak_comparison" when warning.Value is { } fit =>
            $"The comparison only loosely tracked the outcome before the change (fit {fit.ToString("0.00", CultureInfo.InvariantCulture)}; aim for 0.70 or more).",
        "weak_comparison" => "The comparison didn't track the outcome before the change.",
        "few_placebos" => "There isn't enough history to put a range on a change this long.",
        "noisy" => $"The data is noisy, so only changes bigger than about {Whole(warning.Value * 100)}% can be measured reliably.",
        "not_covered" => $"The data covers only {Whole(warning.Value)} of the change's {PlannedDays(analysis)} days so far.",
        _ => "This weakens the result.",
    });

    public static string[] Keys(string json) => Read<string[]>(json) ?? [];

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);

    private static LabResultView? Result(LabAnalysis analysis, LabDataset? dataset)
    {
        if (analysis.Result is not { } json || Read<LabStoredResult>(json) is not { } stored)
        {
            return null;
        }

        var spec = LabSourceSpec.Read(analysis.Source);
        return new LabResultView(
            stored.Status,
            stored.Reason,
            analysis.Outcome ?? LabOutcomes.Inconclusive,
            analysis.Conclusive,
            stored.Figures,
            LabMetrics.IsMoney(spec.Metric) ? dataset?.Currency : null,
            [.. stored.Daily.Select(day => new LabDayView(analysis.Start.AddDays(day.Offset), day.Actual, day.Expected, day.Offset >= 0))],
            analysis.Summary,
            analysis.Explanation,
            analysis.InconclusiveReason,
            analysis.NextAdvice,
            analysis.Method,
            analysis.Model,
            analysis.PromptVersion,
            analysis.FinishedAt);
    }

    private static string Whole(double? value) => Math.Round(value ?? 0).ToString("0", CultureInfo.InvariantCulture);
}
