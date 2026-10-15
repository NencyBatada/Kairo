namespace Kairo.Modules.Catalogue.Imports;

internal sealed class CatalogueSource : IBrandOwned
{
    public Guid BrandId { get; set; }

    public string Kind { get; set; } = SourceKinds.File;

    public string? FeedUrl { get; set; }

    public Guid SetBy { get; set; }

    public DateTimeOffset SetAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? LastSucceededAt { get; set; }

    public int FailuresInRow { get; set; }
}
