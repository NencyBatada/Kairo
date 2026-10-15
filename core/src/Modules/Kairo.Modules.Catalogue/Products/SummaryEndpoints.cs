using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class SummaryEndpoints
{
    public const int ShownCategories = 100;

    public static async Task<Ok<CatalogueSummary>> GetAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        var all = db.BrandProducts.AsNoTracking();
        var active = all.Where(p => p.Status == ProductStatuses.Active);
        var missing = new MissingCounts(
            await CountMissingAsync(active, MissingDetails.Title, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Price, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Link, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Image, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Stock, cancellationToken),
            await CountMissingAsync(active, MissingDetails.All, cancellationToken));
        var categories = await active
            .Select(p => p.Category ?? p.Fixes.Category)
            .Where(name => name != null)
            .GroupBy(name => name)
            .Select(group => new { Name = group.Key, Products = group.Count() })
            .OrderByDescending(entry => entry.Products)
            .ThenBy(entry => entry.Name)
            .Take(ShownCategories)
            .ToListAsync(cancellationToken);
        var lastImport = await db.BrandImports.AsNoTracking()
            .OrderByDescending(import => import.StartedAt)
            .ThenByDescending(import => import.Number)
            .FirstOrDefaultAsync(cancellationToken);
        var source = await db.BrandSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new CatalogueSummary(
            await all.CountAsync(cancellationToken),
            await active.CountAsync(cancellationToken),
            await active.CountAsync(p => p.Availability == Availabilities.InStock, cancellationToken),
            await active.CountAsync(p => p.Availability == Availabilities.OutOfStock, cancellationToken),
            missing,
            categories.Select(entry => new CategoryCount(entry.Name!, entry.Products)).ToArray(),
            lastImport is null ? null : ImportView.Of(lastImport),
            source is null ? null : SourceView.Of(source)));
    }

    private static Task<int> CountMissingAsync(IQueryable<Product> products, MissingDetails flags, CancellationToken cancellationToken) =>
        products.CountAsync(p => (p.Missing & flags) != MissingDetails.None, cancellationToken);
}
