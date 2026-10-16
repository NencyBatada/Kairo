using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.History;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement;

public sealed class MeasurementModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MeasurementDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, MeasurementDbContext.SchemaName));
        services.AddFeeds();
        services.AddScoped<IRegionHistory, RegionHistoryReader>();
        services.AddScoped<DataCoverage>();
        services.AddScoped<LiftMeasurer>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapFeeds();
        api.MapLift();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapFeedJobs();
        jobs.MapLiftJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<MeasurementDbContext>().Database.MigrateAsync(cancellationToken);
}
