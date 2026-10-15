using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Catalogue.Data;

internal sealed class CatalogueDbContextFactory : IDesignTimeDbContextFactory<CatalogueDbContext>
{
    public CatalogueDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<CatalogueDbContext>(
            CatalogueDbContext.SchemaName,
            (options, database) => new CatalogueDbContext(options, database));
}
