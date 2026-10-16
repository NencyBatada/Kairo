using Kairo.Modules.Care.Data;
using Kairo.Modules.Care.Recommendations;

namespace Kairo.Modules.Care;

public sealed class CareModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CareDbContext>((provider, options) => options.UseKairoDatabase(provider, CareDbContext.SchemaName));
        services.AddScoped<RecommendationDrafter>();
        services.AddScoped<RecommendationDesk>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        var brand = api.MapBrandApi();
        brand.MapGet("/campaigns/{campaignId:guid}/recommendations", RecommendationEndpoints.ListAsync);
        var recommendations = brand.MapGroup("/recommendations");
        recommendations.MapPost("/{recommendationId:guid}/approve", RecommendationEndpoints.ApproveAsync).RequireBrandRole(BrandRoles.Approver).WithoutRequestTransaction();
        recommendations.MapPost("/{recommendationId:guid}/dismiss", RecommendationEndpoints.DismissAsync).RequireBrandRole(BrandRoles.Marketer | BrandRoles.Approver);
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapRecommendationJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CareDbContext>().Database.MigrateAsync(cancellationToken);
}
