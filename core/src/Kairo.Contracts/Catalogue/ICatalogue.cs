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
}

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
    string? Currency);
