namespace Kairo.Contracts.Catalogue;

public interface ICatalogue
{
    Task<IReadOnlyList<CatalogueCategory>> ListInStockCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueProduct>> FindInStockAsync(
        IReadOnlyCollection<string> categories,
        IReadOnlyCollection<string> terms,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueProduct>> GetInStockAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task<CatalogueUpdate?> LatestImportAsync(CancellationToken cancellationToken);

    Task<CategoryStock> GetCategoryStockAsync(string category, CancellationToken cancellationToken);
}

public sealed record CatalogueUpdate(Guid ImportId, DateTimeOffset FinishedAt);

public sealed record CategoryStock(int InStockProducts, int ProductsWithQuantity, long Units);

public sealed record CatalogueCategory(string Name, int InStock, IReadOnlyList<string> SampleTitles);

public sealed record CatalogueProduct(
    Guid Id,
    string OfferId,
    string Title,
    string? Category,
    string? Link,
    string? ImageLink,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Description = null,
    string? Brand = null,
    string? Gtin = null,
    IReadOnlyList<string>? AdditionalImageLinks = null,
    string? Availability = null);
