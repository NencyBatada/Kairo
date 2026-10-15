namespace Kairo.Modules.Catalogue.Imports;

internal sealed class CatalogueImport : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public long Number { get; set; }

    public string Kind { get; set; } = SourceKinds.File;

    public string? FileName { get; set; }

    public string? FeedUrl { get; set; }

    public string Status { get; set; } = ImportStatuses.Failed;

    public int Rows { get; set; }

    public int Valid { get; set; }

    public int Invalid { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Removed { get; set; }

    public int Unchanged { get; set; }

    public string Problems { get; set; } = "[]";

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ContentHash { get; set; }

    public string? StorageKey { get; set; }

    public Guid? StartedBy { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }
}
