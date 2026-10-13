using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Measurement.Data;

internal sealed class MeasurementDbContextFactory : IDesignTimeDbContextFactory<MeasurementDbContext>
{
    public MeasurementDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<MeasurementDbContext>(
            MeasurementDbContext.SchemaName,
            (options, database) => new MeasurementDbContext(options, database));
}
