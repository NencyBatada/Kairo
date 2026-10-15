using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Opportunities.Data;

internal sealed class OpportunitiesDbContextFactory : IDesignTimeDbContextFactory<OpportunitiesDbContext>
{
    public OpportunitiesDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<OpportunitiesDbContext>(
            OpportunitiesDbContext.SchemaName,
            (options, database) => new OpportunitiesDbContext(options, database));
}
