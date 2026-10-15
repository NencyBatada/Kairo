using Kairo.Contracts.Brands;
using Kairo.Modules.Opportunities.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Opportunities.Refresh;

internal sealed record RefreshSummary(int Brands, BrandRefresh[] Results);

internal sealed class RefreshRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<RefreshRunner> logger)
{
    public const int BrandsAtOnce = 4;

    public static readonly TimeSpan KeepRunsFor = TimeSpan.FromDays(90);

    public async Task<RefreshSummary> RunAsync(IReadOnlyList<BrandSummary> brands, bool onlyDue, CancellationToken cancellationToken)
    {
        var targets = onlyDue ? await DueAsync(brands, cancellationToken) : brands.ToList();
        var results = new BrandRefresh[targets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            new ParallelOptions { MaxDegreeOfParallelism = BrandsAtOnce, CancellationToken = cancellationToken },
            async (index, token) => results[index] = await RefreshBrandAsync(targets[index], token));
        await DeleteOldRunsAsync(cancellationToken);
        return new RefreshSummary(targets.Count, results);
    }

    private async Task<BrandRefresh> RefreshBrandAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, cancellationToken);
            return await scope.ServiceProvider.GetRequiredService<BrandRefresher>().RefreshAsync(brand, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The opportunity refresh failed for brand {BrandId}.", brand.Id);
            return new BrandRefresh(brand.Id, RefreshOutcomes.Error, null, 0, 0, 0);
        }
    }

    private async Task<List<BrandSummary>> DueAsync(IReadOnlyList<BrandSummary> brands, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        Dictionary<Guid, DateTimeOffset> lastSuccess;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            lastSuccess = await scope.ServiceProvider.GetRequiredService<OpportunitiesDbContext>().RefreshRuns
                .Where(run => run.Outcome == RefreshOutcomes.Succeeded)
                .GroupBy(run => run.BrandId)
                .Select(runs => new { BrandId = runs.Key, At = runs.Max(run => run.StartedAt) })
                .ToDictionaryAsync(entry => entry.BrandId, entry => entry.At, cancellationToken);
        }

        var now = time.GetUtcNow();
        return brands
            .Where(brand => RefreshClock.IsDue(now, brand.TimeZone, lastSuccess.TryGetValue(brand.Id, out var at) ? at : null))
            .OrderBy(brand => lastSuccess.TryGetValue(brand.Id, out var at) ? at : DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task DeleteOldRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var cutoff = time.GetUtcNow() - KeepRunsFor;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            await scope.ServiceProvider.GetRequiredService<OpportunitiesDbContext>().RefreshRuns
                .Where(run => run.StartedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
