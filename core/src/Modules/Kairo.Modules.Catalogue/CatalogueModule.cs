using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue;

public sealed class CatalogueModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogueDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, CatalogueDbContext.SchemaName));
        services.AddCatalogue();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapCatalogue();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogueDbContext>().Database.MigrateAsync(cancellationToken);
}
