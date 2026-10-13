using Kairo.Contracts.Brands;
using Kairo.Modules.Brands.Connections;
using Kairo.Modules.Brands.Data;
using Kairo.Modules.Brands.Workspaces;

namespace Kairo.Modules.Brands;

public sealed class BrandsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BrandsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, BrandsDbContext.SchemaName));
        services.AddScoped<IBrandDirectory, BrandDirectory>();
        services.AddConnections(configuration);
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapWorkspaces();
        api.MapConnections();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandsDbContext>().Database.MigrateAsync(cancellationToken);
}
