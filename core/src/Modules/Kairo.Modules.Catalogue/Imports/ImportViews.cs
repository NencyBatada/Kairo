using System.Text.Json;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Imports;

internal static class ImportProblems
{
    public static string Write(IReadOnlyList<ImportProblem> problems) => JsonSerializer.Serialize(problems, JsonSerializerOptions.Web);

    public static ImportProblem[] Read(string json) => JsonSerializer.Deserialize<ImportProblem[]>(json, JsonSerializerOptions.Web) ?? [];
}

internal sealed record ImportView(
    Guid Id,
    string Kind,
    string? FileName,
    string? FeedUrl,
    string Status,
    int Rows,
    int Valid,
    int Invalid,
    int Added,
    int Updated,
    int Removed,
    int Unchanged,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt)
{
    public static ImportView Of(CatalogueImport import) => new(
        import.Id,
        import.Kind,
        import.FileName,
        import.FeedUrl,
        import.Status,
        import.Rows,
        import.Valid,
        import.Invalid,
        import.Added,
        import.Updated,
        import.Removed,
        import.Unchanged,
        import.ErrorCode,
        import.ErrorMessage,
        import.StartedBy,
        import.StartedAt,
        import.FinishedAt);
}

internal sealed record ImportDetailView(
    Guid Id,
    string Kind,
    string? FileName,
    string? FeedUrl,
    string Status,
    int Rows,
    int Valid,
    int Invalid,
    int Added,
    int Updated,
    int Removed,
    int Unchanged,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ImportProblem[] Problems)
{
    public static ImportDetailView Of(CatalogueImport import) => new(
        import.Id,
        import.Kind,
        import.FileName,
        import.FeedUrl,
        import.Status,
        import.Rows,
        import.Valid,
        import.Invalid,
        import.Added,
        import.Updated,
        import.Removed,
        import.Unchanged,
        import.ErrorCode,
        import.ErrorMessage,
        import.StartedBy,
        import.StartedAt,
        import.FinishedAt,
        ImportProblems.Read(import.Problems));
}
