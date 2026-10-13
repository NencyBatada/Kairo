using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Live.Data;

internal sealed class LiveDbContextFactory : IDesignTimeDbContextFactory<LiveDbContext>
{
    public LiveDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<LiveDbContext>(
            LiveDbContext.SchemaName,
            (options, database) => new LiveDbContext(options, database));
}
