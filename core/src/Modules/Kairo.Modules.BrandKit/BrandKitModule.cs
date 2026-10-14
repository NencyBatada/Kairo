using Kairo.Modules.BrandKit.Data;

namespace Kairo.Modules.BrandKit;

public sealed class BrandKitModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BrandKitDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, BrandKitDbContext.SchemaName));
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandKitDbContext>().Database.MigrateAsync(cancellationToken);
}
