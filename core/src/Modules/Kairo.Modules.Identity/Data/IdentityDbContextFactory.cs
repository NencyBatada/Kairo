using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Identity.Data;

internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<IdentityDbContext>(
            IdentityDbContext.SchemaName,
            (options, database) => new IdentityDbContext(options, database));
}
