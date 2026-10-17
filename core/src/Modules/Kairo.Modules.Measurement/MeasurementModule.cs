using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.History;
using Kairo.Modules.Measurement.Impact;
using Kairo.Modules.Measurement.Lab;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement;

public sealed class MeasurementModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kairo.Contracts.Ask.IAskResource, Kairo.Modules.Measurement.Impact.ReportAskResource>();
        services.AddScoped<Kairo.Contracts.Ask.IAskResource, Kairo.Modules.Measurement.Impact.LabAskResource>();
        services.AddDbContext<MeasurementDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, MeasurementDbContext.SchemaName));
        services.AddFeeds();
        services.AddScoped<IRegionHistory, RegionHistoryReader>();
        services.AddScoped<DataCoverage>();
        services.AddScoped<LiftMeasurer>();
        services.AddScoped<AccountActivity>();
        services.AddScoped<ImpactReporter>();
        services.AddScoped<IImpactReports, ImpactReports>();
        services.AddLab();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapFeeds();
        api.MapLift();
        api.MapImpact();
        api.MapLab();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapFeedJobs();
        jobs.MapLiftJobs();
        jobs.MapLabJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<MeasurementDbContext>().Database.MigrateAsync(cancellationToken);
}
