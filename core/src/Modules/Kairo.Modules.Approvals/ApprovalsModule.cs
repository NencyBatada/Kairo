using Kairo.Contracts.Approvals;
using Kairo.Modules.Approvals.Data;

namespace Kairo.Modules.Approvals;

public sealed class ApprovalsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApprovalsDbContext>((provider, options) => options.UseKairoDatabase(provider, ApprovalsDbContext.SchemaName));
        services.AddScoped<IApprovalLocks, ApprovalLocks>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<ApprovalsDbContext>().Database.MigrateAsync(cancellationToken);
}
