using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue;

public sealed class CatalogueModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogueDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, CatalogueDbContext.SchemaName));
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogueDbContext>().Database.MigrateAsync(cancellationToken);
}
