using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Data;

namespace Kairo.Modules.Triggers;

public sealed class TriggersModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TriggersDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, TriggersDbContext.SchemaName));
        services.AddTriggers();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapTriggers();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapTriggerJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<TriggersDbContext>().Database.MigrateAsync(cancellationToken);
}
