using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.History;

namespace Kairo.Modules.Measurement;

public sealed class MeasurementModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MeasurementDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, MeasurementDbContext.SchemaName));
        services.AddFeeds();
        services.AddScoped<IRegionHistory, RegionHistoryReader>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapFeeds();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapFeedJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<MeasurementDbContext>().Database.MigrateAsync(cancellationToken);
}
