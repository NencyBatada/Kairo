using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.BrandKit.Data;

internal sealed class BrandKitDbContextFactory : IDesignTimeDbContextFactory<BrandKitDbContext>
{
    public BrandKitDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<BrandKitDbContext>(
            BrandKitDbContext.SchemaName,
            (options, database) => new BrandKitDbContext(options, database));
}
