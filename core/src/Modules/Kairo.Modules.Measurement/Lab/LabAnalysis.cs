namespace Kairo.Modules.Measurement.Lab;

internal sealed class LabAnalysis : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Name { get; set; } = "";

    public string WhatChanged { get; set; } = "";

    public DateOnly Start { get; set; }

    public DateOnly End { get; set; }

    public string AffectsKind { get; set; } = "";

    public string Source { get; set; } = "{}";

    public string State { get; set; } = LabStates.Setup;

    public DateOnly? DataFrom { get; set; }

    public DateOnly? DataTo { get; set; }

    public int? HistoryDays { get; set; }

    public int? ChangeDays { get; set; }

    public int SeriesCount { get; set; }

    public string Affected { get; set; } = "[]";

    public string Comparison { get; set; } = "[]";

    public string Candidates { get; set; } = "[]";

    public double? Fit { get; set; }

    public double? Detectable { get; set; }

    public int Placebos { get; set; }

    public string Warnings { get; set; } = "[]";

    public string? Outcome { get; set; }

    public bool Conclusive { get; set; }

    public string? Result { get; set; }

    public string? Summary { get; set; }

    public string? Explanation { get; set; }

    public string? InconclusiveReason { get; set; }

    public string? NextAdvice { get; set; }

    public string? Method { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

    public string? Problem { get; set; }

    public Guid? RunBy { get; set; }

    public DateTimeOffset? RunAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? MemoryEntryId { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; } = 1;
}

internal sealed class LabDataset : IBrandOwned
{
    public Guid AnalysisId { get; set; }

    public Guid BrandId { get; set; }

    public string? FileName { get; set; }

    public DateOnly? UploadFrom { get; set; }

    public DateOnly? UploadTo { get; set; }

    public string? Upload { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? Currency { get; set; }

    public string? Series { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal static class LabStates
{
    public const string Setup = "setup";
    public const string DataCheck = "data_check";
    public const string Running = "running";
    public const string Result = "result";
    public const string Failed = "failed";
}

internal static class LabAffects
{
    public const string Regions = "regions";
    public const string Products = "products";
    public const string Other = "other";

    public static readonly string[] All = [Regions, Products, Other];
}

internal static class LabOutcomes
{
    public const string Rose = "rose";
    public const string Fell = "fell";
    public const string Inconclusive = "inconclusive";
}
