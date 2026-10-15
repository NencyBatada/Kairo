using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Catalogue.Refresh;

internal static class RefreshJobs
{
    public static void MapCatalogueJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/catalogue/refresh", RefreshAsync);

    private static async Task<Ok<RefreshRun>> RefreshAsync(Guid? brandId, RefreshRunner runner, IHostApplicationLifetime lifetime) =>
        TypedResults.Ok(await runner.RunAsync(brandId, lifetime.ApplicationStopping));
}
