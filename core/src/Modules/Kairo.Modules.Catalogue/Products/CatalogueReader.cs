using Kairo.Contracts.Catalogue;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal sealed class CatalogueReader(CatalogueDbContext db) : ICatalogue
{
    public const int MaxCategories = 100;
    public const int SampleTitles = 3;
    public const int MaxCandidates = 500;

    public async Task<IReadOnlyList<CatalogueCategory>> ListInStockCategoriesAsync(CancellationToken cancellationToken)
    {
        var brandId = db.BrandId;
        var rows = await db.Database.SqlQuery<CategorySample>(
            $"""
            WITH Effective AS (
                SELECT COALESCE(Category, JSON_VALUE(Fixes, '$.category')) AS Name,
                       COALESCE(Title, JSON_VALUE(Fixes, '$.title')) AS Title
                FROM catalogue.Products
                WHERE BrandId = {brandId} AND Status = {ProductStatuses.Active} AND Availability = {Availabilities.InStock}
            ),
            Ranked AS (
                SELECT Name, Title,
                       ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Title) AS Position,
                       COUNT(*) OVER (PARTITION BY Name) AS InStock
                FROM Effective
                WHERE Name IS NOT NULL
            )
            SELECT Name, Title, InStock FROM Ranked WHERE Position <= {SampleTitles}
            """).ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new CatalogueCategory(
                group.Key,
                group.First().InStock,
                group.Select(row => row.Title).OfType<string>().ToList()))
            .OrderByDescending(category => category.InStock)
            .ThenBy(category => category.Name, StringComparer.Ordinal)
            .Take(MaxCategories)
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogueProduct>> FindInStockAsync(
        IReadOnlyCollection<string> categories,
        IReadOnlyCollection<string> terms,
        int limit,
        CancellationToken cancellationToken)
    {
        var names = categories.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var candidates = await InStock()
            .Where(p => names.Contains((p.Category ?? p.Fixes.Category)!))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);
        var words = Words(terms);
        return candidates
            .Select(product => (Product: product, Hits: Words([TitleOf(product)]).Count(words.Contains)))
            .OrderByDescending(entry => entry.Hits)
            .ThenBy(entry => TitleOf(entry.Product), StringComparer.Ordinal)
            .Take(limit)
            .Select(entry => Of(entry.Product))
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogueProduct>> GetInStockAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToList();
        var products = await InStock().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        return products.Select(Of).ToList();
    }

    public async Task<CatalogueUpdate?> LatestImportAsync(CancellationToken cancellationToken)
    {
        var latest = await db.BrandImports.AsNoTracking()
            .Where(import => import.Status == ImportStatuses.Succeeded)
            .OrderByDescending(import => import.Number)
            .Select(import => new { import.Id, import.FinishedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : new CatalogueUpdate(latest.Id, latest.FinishedAt);
    }

    public async Task<CategoryStock> GetCategoryStockAsync(string category, CancellationToken cancellationToken)
    {
        var quantities = await InStock()
            .Where(p => (p.Category ?? p.Fixes.Category) == category)
            .Select(p => p.Quantity)
            .ToListAsync(cancellationToken);
        var known = quantities.OfType<int>().ToList();
        return new CategoryStock(quantities.Count, known.Count, known.Sum(quantity => (long)quantity));
    }

    private IQueryable<Product> InStock() =>
        db.BrandProducts.AsNoTracking().Where(p => p.Status == ProductStatuses.Active && p.Availability == Availabilities.InStock);

    private static string TitleOf(Product product) => product.Title ?? product.Fixes.Title ?? product.OfferId;

    private static HashSet<string> Words(IEnumerable<string> texts) =>
        texts
            .SelectMany(text => text.Split([' ', ',', '-', '/', '>', '&', '(', ')', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(word => word.Length > 2)
            .Select(word => word.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    private static CatalogueProduct Of(Product product)
    {
        var view = ProductView.Of(product);
        return new CatalogueProduct(
            product.Id,
            product.OfferId,
            TitleOf(product),
            view.Category,
            view.Link,
            view.ImageLink,
            view.Price,
            view.SalePrice,
            view.Currency,
            view.Description,
            view.Brand,
            view.Gtin,
            view.AdditionalImageLinks,
            view.Availability);
    }
}

internal sealed record CategorySample(string Name, string? Title, int InStock);
