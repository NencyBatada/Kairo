using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int ShownStockEvents = 20;
    public const string AuditTarget = "catalogue_product";

    public static async Task<Results<Ok<ProductPage>, ValidationProblem>> ListAsync(
        CatalogueDbContext db,
        int? page,
        int? pageSize,
        string? search,
        string? category,
        string? availability,
        string? missing,
        string? status,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = page ?? 1;
        if (pageNumber < 1)
        {
            errors["page"] = ["Use a page number from 1."];
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["Use a page size from 1 to 200."];
        }

        var wanted = status ?? ProductStatuses.Active;
        if (wanted is not (ProductStatuses.Active or ProductStatuses.Removed))
        {
            errors["status"] = ["Use active or removed."];
        }

        if (availability is not null && !Availabilities.All.Contains(availability))
        {
            errors["availability"] = ["Use in_stock, out_of_stock, preorder or backorder."];
        }

        var gaps = missing is null ? null : ProductFields.ParseMissing(missing);
        if (missing is not null && gaps is null)
        {
            errors["missing"] = ["Use any, title, price, link, image or stock."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = db.BrandProducts.AsNoTracking().Where(p => p.Status == wanted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.OfferId.Contains(term) || (p.Title ?? p.Fixes.Title)!.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var name = category.Trim();
            query = query.Where(p => (p.Category ?? p.Fixes.Category) == name);
        }

        if (availability is not null)
        {
            query = query.Where(p => p.Availability == availability);
        }

        if (gaps is { } flags)
        {
            query = query.Where(p => (p.Missing & flags) != MissingDetails.None);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Title ?? p.Fixes.Title)
            .ThenBy(p => p.OfferId)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ProductPage(items.Select(ProductView.Of).ToArray(), pageNumber, size, total));
    }

    public static async Task<Results<Ok<ProductDetailView>, NotFound>> GetAsync(
        Guid productId,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.BrandProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken) is not { } product)
        {
            return TypedResults.NotFound();
        }

        var events = await db.BrandStockEvents.AsNoTracking()
            .Where(stock => stock.ProductId == productId)
            .OrderByDescending(stock => stock.At)
            .Take(ShownStockEvents)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ProductDetailView(
            ProductView.Of(product),
            FixesView.Of(product.Fixes),
            events.Select(stock => new StockEventView(stock.From, stock.To, stock.ImportId, stock.At)).ToArray()));
    }

    public static async Task<Results<Ok<ProductView>, NotFound, ValidationProblem, ProblemHttpResult>> FixAsync(
        Guid productId,
        JsonObject body,
        CatalogueDbContext db,
        CatalogueImporter importer,
        IBrandDirectory brands,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        if (await db.BrandProducts.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken) is not { } product)
        {
            return TypedResults.NotFound();
        }

        var currency = (await brands.GetAsync([member.BrandId], cancellationToken)).Single().Currency;
        var before = FixesView.Of(product.Fixes);
        var errors = ProductFixRequest.Apply(body, product, currency);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        product.RefreshMissing();
        product.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var after = FixesView.Of(product.Fixes);
        await audit.RecordAsync(new AuditRecord("catalogue.product_fixed", AuditTarget, product.Id.ToString(), before, after), cancellationToken);
        await live.PublishAsync(CatalogueLiveEvents.ProductChanged(product.Id), cancellationToken);
        return TypedResults.Ok(ProductView.Of(product));
    }
}
