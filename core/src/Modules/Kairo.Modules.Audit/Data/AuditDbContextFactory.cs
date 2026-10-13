using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Audit.Data;

internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<AuditDbContext>(
            AuditDbContext.SchemaName,
            (options, database) => new AuditDbContext(options, database));
}
