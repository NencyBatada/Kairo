using Kairo.Contracts.Brands;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Opportunities.Refresh;

internal static class RefreshJobs
{
    public static void MapOpportunityJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/opportunities/refresh", RefreshAsync);

    private static async Task<Results<Ok<RefreshSummary>, NotFound>> RefreshAsync(
        Guid? brandId,
        IBrandDirectory brands,
        RefreshRunner runner,
        IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var targets = brandId is { } id ? await brands.GetAsync([id], stopping) : await brands.ListAllAsync(stopping);
        if (brandId is not null && targets.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await runner.RunAsync(targets, brandId is null, stopping));
    }
}
