using Kairo.Contracts.Brands;
using Kairo.Modules.Measurement.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class FeedRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<FeedRunner> logger)
{
    public static readonly TimeSpan KeepRecordsFor = TimeSpan.FromDays(90);

    public async Task<DailyRun> RunAsync(IReadOnlyList<BrandSummary> brands, CancellationToken cancellationToken)
    {
        var outcomes = new List<FeedOutcome>();
        foreach (var brand in brands)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, cancellationToken);
                outcomes.AddRange(await scope.ServiceProvider.GetRequiredService<BrandFeeds>().RunAsync(brand.Id, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The data feeds failed for brand {BrandId}.", brand.Id);
            }
        }

        await DeleteOldRecordsAsync(cancellationToken);
        return new DailyRun(brands.Count, outcomes);
    }

    private async Task DeleteOldRecordsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var cutoff = time.GetUtcNow() - KeepRecordsFor;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().FeedRuns
                .Where(run => run.StartedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
