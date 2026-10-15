using Kairo.Contracts.Brands;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Triggers.Checks;

internal static class CheckJobs
{
    public static void MapTriggerJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/triggers/check", CheckAsync);

    private static async Task<Results<Ok<CheckSummary>, NotFound>> CheckAsync(
        Guid? brandId,
        bool? force,
        IBrandDirectory brands,
        CheckRunner runner,
        IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var targets = brandId is { } id ? await brands.GetAsync([id], stopping) : await brands.ListAllAsync(stopping);
        if (brandId is not null && targets.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await runner.RunAsync(targets, force == true, stopping));
    }
}
