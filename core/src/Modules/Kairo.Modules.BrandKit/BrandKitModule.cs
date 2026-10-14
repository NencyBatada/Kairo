using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit;

public sealed class BrandKitModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BrandKitDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, BrandKitDbContext.SchemaName));
        services.AddBrandKit();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapBrandKit();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandKitDbContext>().Database.MigrateAsync(cancellationToken);
}
