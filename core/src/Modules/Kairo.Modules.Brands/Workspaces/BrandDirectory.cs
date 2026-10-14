using Kairo.Contracts.Brands;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Workspaces;

internal sealed class BrandDirectory(BrandsDbContext db, KairoDatabase database) : IBrandDirectory
{
    public async Task<IReadOnlyList<BrandSummary>> GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken)
    {
        var ids = brandIds.ToList();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Brands
                .Where(brand => ids.Contains(brand.Id))
                .OrderBy(brand => brand.Name)
                .Select(brand => new BrandSummary(brand.Id, brand.Handle, brand.Name, brand.Currency))
                .ToListAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<BrandSummary>> ListAllAsync(CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Brands
                .OrderBy(brand => brand.Name)
                .ThenBy(brand => brand.Id)
                .Select(brand => new BrandSummary(brand.Id, brand.Handle, brand.Name, brand.Currency))
                .ToListAsync(cancellationToken);
        }
    }
}
