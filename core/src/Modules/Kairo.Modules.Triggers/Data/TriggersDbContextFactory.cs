using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Triggers.Data;

internal sealed class TriggersDbContextFactory : IDesignTimeDbContextFactory<TriggersDbContext>
{
    public TriggersDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<TriggersDbContext>(
            TriggersDbContext.SchemaName,
            (options, database) => new TriggersDbContext(options, database));
}
