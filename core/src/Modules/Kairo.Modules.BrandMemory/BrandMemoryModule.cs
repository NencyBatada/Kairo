using Kairo.Contracts.BrandMemory;
using Kairo.Modules.BrandMemory.Data;
using Kairo.Modules.BrandMemory.Learning;
using Kairo.Modules.BrandMemory.Memory;

namespace Kairo.Modules.BrandMemory;

public sealed class BrandMemoryModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kairo.Contracts.Ask.IAskResource, Kairo.Modules.BrandMemory.Memory.MemoryAskResource>();
        services.AddDbContext<BrandMemoryDbContext>((provider, options) => options.UseKairoDatabase(provider, BrandMemoryDbContext.SchemaName));
        services.AddScoped<IBrandMemory, BrandMemoryReader>();
        services.AddScoped<LearnAgent>();
        services.AddScoped<LearnWork>();
        services.AddScoped<LearnSweeper>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        var memory = api.MapBrandApi().MapGroup("/brand-memory");
        memory.MapGet("", MemoryEndpoints.ListAsync);
        memory.MapGet("/{entryId:guid}", MemoryEndpoints.GetAsync);
        memory.MapPatch("/{entryId:guid}", MemoryEndpoints.EditAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);
        memory.MapDelete("/{entryId:guid}", MemoryEndpoints.RemoveAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapLearnJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandMemoryDbContext>().Database.MigrateAsync(cancellationToken);
}
