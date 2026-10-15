using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal record ProductView(
    Guid Id,
    string OfferId,
    string? Title,
    string? Description,
    string? Link,
    string? ImageLink,
    string[] AdditionalImageLinks,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Availability,
    string? Category,
    string? GoogleCategory,
    string? Brand,
    string? Gtin,
    string? ItemGroupId,
    string[] Fixed,
    string[] Missing,
    string Status,
    DateTimeOffset UpdatedAt)
{
    public static ProductView Of(Product product) => new(
        product.Id,
        product.OfferId,
        product.Title ?? product.Fixes.Title,
        product.Description,
        product.Link ?? product.Fixes.Link,
        product.ImageLink ?? product.Fixes.ImageLink,
        [.. product.AdditionalImageLinks],
        ProductFields.Plain(product.Price ?? product.Fixes.Price),
        ProductFields.Plain(product.SalePrice),
        product.Price is null ? product.Fixes.Currency : product.Currency,
        product.Availability,
        product.Category ?? product.Fixes.Category,
        product.GoogleCategory,
        product.Brand,
        product.Gtin,
        product.ItemGroupId,
        ProductFields.Fixed(product.Fixes),
        ProductFields.Missing(product.Missing),
        product.Status,
        product.UpdatedAt);
}

internal sealed record FixesView(string? Title, decimal? Price, string? Currency, string? Link, string? ImageLink, string? Category)
{
    public static FixesView Of(ProductFixes fixes) =>
        new(fixes.Title, ProductFields.Plain(fixes.Price), fixes.Currency, fixes.Link, fixes.ImageLink, fixes.Category);
}

internal sealed record StockEventView(string? From, string? To, Guid ImportId, DateTimeOffset At);

internal sealed record ProductDetailView : ProductView
{
    public ProductDetailView(ProductView product, FixesView fixes, StockEventView[] stockEvents)
        : base(product)
    {
        Fixes = fixes;
        StockEvents = stockEvents;
    }

    public FixesView Fixes { get; }

    public StockEventView[] StockEvents { get; }
}

internal sealed record ProductPage(ProductView[] Items, int Page, int PageSize, int Total);

internal sealed record MissingCounts(int Title, int Price, int Link, int Image, int Stock, int Any);

internal sealed record CategoryCount(string Name, int Products);

internal sealed record CatalogueSummary(
    int Products,
    int Active,
    int InStock,
    int OutOfStock,
    MissingCounts Missing,
    CategoryCount[] Categories,
    ImportView? LastImport,
    SourceView? Source);
