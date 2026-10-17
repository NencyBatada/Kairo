using Kairo.Modules.Opportunities.Data;
using Kairo.Modules.Opportunities.Refresh;

namespace Kairo.Modules.Opportunities;

public sealed class OpportunitiesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kairo.Contracts.Ask.IAskResource, Kairo.Modules.Opportunities.Radar.OpportunityAskResource>();
        services.AddDbContext<OpportunitiesDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, OpportunitiesDbContext.SchemaName));
        services.AddOpportunities();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapOpportunities();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapOpportunityJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<OpportunitiesDbContext>().Database.MigrateAsync(cancellationToken);
}
