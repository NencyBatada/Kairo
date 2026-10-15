using Kairo.Contracts.Brands;
using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Weather;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Triggers.Checks;

internal sealed record CheckSummary(int Brands, BrandCheck[] Results);

internal sealed class CheckRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<CheckRunner> logger)
{
    public const int BrandsAtOnce = 4;

    public async Task<CheckSummary> RunAsync(IReadOnlyList<BrandSummary> brands, bool force, CancellationToken cancellationToken)
    {
        var results = new BrandCheck[brands.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, brands.Count),
            new ParallelOptions { MaxDegreeOfParallelism = BrandsAtOnce, CancellationToken = cancellationToken },
            async (index, token) => results[index] = await CheckBrandAsync(brands[index], force, token));
        await DeleteOldForecastsAsync(cancellationToken);
        return new CheckSummary(brands.Count, results);
    }

    private async Task<BrandCheck> CheckBrandAsync(BrandSummary brand, bool force, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, cancellationToken);
            return await scope.ServiceProvider.GetRequiredService<BrandChecker>().CheckAsync(brand, force, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The trigger check failed for brand {BrandId}.", brand.Id);
            return new BrandCheck(brand.Id, BrandCheckOutcomes.Error, 0, 0, 0, 0);
        }
    }

    private async Task DeleteOldForecastsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var cutoff = time.GetUtcNow() - ForecastCache.KeepFor;
        await scope.ServiceProvider.GetRequiredService<TriggersDbContext>().Forecasts
            .Where(forecast => forecast.Hour < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
