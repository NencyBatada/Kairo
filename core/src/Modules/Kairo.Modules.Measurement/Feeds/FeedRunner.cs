using Kairo.Contracts.Brands;
using Kairo.Modules.Measurement.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class FeedRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<FeedRunner> logger)
{
    public const int BrandsAtOnce = 4;

    public static readonly TimeSpan KeepRecordsFor = TimeSpan.FromDays(90);

    public async Task<DailyRun> RunAsync(IReadOnlyList<BrandSummary> brands, CancellationToken cancellationToken)
    {
        var ordered = await OldestFirstAsync(brands, cancellationToken);
        var outcomes = new IReadOnlyList<FeedOutcome>[ordered.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, ordered.Count),
            new ParallelOptions { MaxDegreeOfParallelism = BrandsAtOnce, CancellationToken = cancellationToken },
            async (index, token) => outcomes[index] = await RunBrandAsync(ordered[index], token));

        await DeleteOldRecordsAsync(cancellationToken);
        return new DailyRun(brands.Count, outcomes.SelectMany(brand => brand).ToList());
    }

    private async Task<IReadOnlyList<FeedOutcome>> RunBrandAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, cancellationToken);
            return await scope.ServiceProvider.GetRequiredService<BrandFeeds>().RunAsync(brand.Id, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The data feeds failed for brand {BrandId}.", brand.Id);
            return [];
        }
    }

    private async Task<List<BrandSummary>> OldestFirstAsync(IReadOnlyList<BrandSummary> brands, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        Dictionary<Guid, DateTimeOffset> lastSuccess;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            lastSuccess = await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().FeedRuns
                .Where(run => run.Status == RunStatuses.Succeeded)
                .GroupBy(run => run.BrandId)
                .Select(runs => new { BrandId = runs.Key, At = runs.Max(run => run.StartedAt) })
                .ToDictionaryAsync(entry => entry.BrandId, entry => entry.At, cancellationToken);
        }

        return brands.OrderBy(brand => lastSuccess.TryGetValue(brand.Id, out var at) ? at : DateTimeOffset.MinValue).ToList();
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
