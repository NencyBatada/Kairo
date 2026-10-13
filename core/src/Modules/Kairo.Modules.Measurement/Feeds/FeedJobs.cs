using Kairo.Contracts.Brands;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedJobs
{
    public static void MapFeedJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/analytics/daily", DailyAsync);

    private static async Task<Results<Ok<DailyRun>, NotFound, ProblemHttpResult>> DailyAsync(
        Guid? brandId,
        AnalyticsOptions options,
        IBrandDirectory brands,
        FeedRunner runner,
        IHostApplicationLifetime lifetime)
    {
        if (!options.Enabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AnalyticsOptions.NotSetUp);
        }

        var stopping = lifetime.ApplicationStopping;
        var targets = brandId is { } id ? await brands.GetAsync([id], stopping) : await brands.ListAllAsync(stopping);
        if (brandId is not null && targets.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await runner.RunAsync(targets, stopping));
    }
}
