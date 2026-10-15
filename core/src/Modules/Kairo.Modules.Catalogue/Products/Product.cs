namespace Kairo.Modules.Catalogue.Products;

internal sealed class Product : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string OfferId { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public List<string> AdditionalImageLinks { get; set; } = [];

    public decimal? Price { get; set; }

    public decimal? SalePrice { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public string? Category { get; set; }

    public string? GoogleCategory { get; set; }

    public string? Brand { get; set; }

    public string? Gtin { get; set; }

    public string? ItemGroupId { get; set; }

    public ProductFixes Fixes { get; set; } = new();

    public MissingDetails Missing { get; set; }

    public string Status { get; set; } = ProductStatuses.Active;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid LastImportId { get; set; }
}
