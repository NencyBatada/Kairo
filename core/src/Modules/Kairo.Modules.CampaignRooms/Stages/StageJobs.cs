using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.CampaignRooms.Stages;

internal sealed record StageSweep(int Processed);

internal static class StageJobs
{
    public const int MaxRounds = 50;

    public static void MapStageJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/campaigns/stage-runs", SweepAsync);

    private static async Task<Ok<StageSweep>> SweepAsync(StageDispatcher dispatcher, IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var processed = 0;
        for (var round = 0; round < MaxRounds; round++)
        {
            var claimed = await dispatcher.RunDueAsync(stopping);
            if (claimed == 0)
            {
                break;
            }

            processed += claimed;
        }

        return TypedResults.Ok(new StageSweep(processed));
    }
}
