using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

public sealed class AuditModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, AuditDbContext.SchemaName));
        services.AddScoped<IAuditLog, AuditLog>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<AuditDbContext>().Database.MigrateAsync(cancellationToken);
}
