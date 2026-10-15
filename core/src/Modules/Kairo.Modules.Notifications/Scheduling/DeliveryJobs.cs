using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Notifications.Scheduling;

internal static class DeliveryJobs
{
    public static void MapDeliveryJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/notifications/due", DueAsync);

    private static async Task<Ok<DueRun>> DueAsync(DueDeliveries deliveries, IHostApplicationLifetime lifetime) =>
        TypedResults.Ok(await deliveries.RunAsync(lifetime.ApplicationStopping));
}
