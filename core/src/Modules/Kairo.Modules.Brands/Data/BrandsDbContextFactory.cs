using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContextFactory : IDesignTimeDbContextFactory<BrandsDbContext>
{
    public BrandsDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<BrandsDbContext>(
            BrandsDbContext.SchemaName,
            (options, database) => new BrandsDbContext(options, database));
}
