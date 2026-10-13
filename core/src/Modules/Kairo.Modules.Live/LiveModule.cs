using Kairo.Contracts.Live;
using Kairo.Modules.Live.Data;
using Kairo.Modules.Live.Publishing;

namespace Kairo.Modules.Live;

public sealed class LiveModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LiveDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, LiveDbContext.SchemaName));
        services.AddScoped<ILiveUpdates, LiveUpdates>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<LiveDbContext>().Database.MigrateAsync(cancellationToken);
}
