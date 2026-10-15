using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Catalogue.Refresh;

internal sealed record BrandRefresh(Guid BrandId, string Outcome, Guid? ImportId);

internal sealed record RefreshRun(int Brands, BrandRefresh[] Results);

internal static class RefreshOutcomes
{
    public const string Skipped = "skipped";
    public const string Error = "error";
}

internal sealed class RefreshRunner(IServiceScopeFactory scopes, ILogger<RefreshRunner> logger)
{
    public const int BrandsAtOnce = 4;

    public async Task<RefreshRun> RunAsync(Guid? brandId, CancellationToken cancellationToken)
    {
        var brandIds = await FeedBrandsAsync(brandId, cancellationToken);
        var results = new BrandRefresh[brandIds.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, brandIds.Count),
            new ParallelOptions { MaxDegreeOfParallelism = BrandsAtOnce, CancellationToken = cancellationToken },
            async (index, token) => results[index] = await RefreshBrandAsync(brandIds[index], token));
        return new RefreshRun(brandIds.Count, results);
    }

    private async Task<List<Guid>> FeedBrandsAsync(Guid? brandId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await scope.ServiceProvider.GetRequiredService<CatalogueDbContext>().Sources
                .Where(source => source.Kind == SourceKinds.Feed && (brandId == null || source.BrandId == brandId))
                .Select(source => source.BrandId)
                .ToListAsync(cancellationToken);
        }
    }

    private async Task<BrandRefresh> RefreshBrandAsync(Guid brandId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, cancellationToken);
            await database.BeginTransactionAsync(cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<CatalogueDbContext>();
            if (await db.BrandSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed, FeedUrl: { } url })
            {
                await database.RollbackAsync(cancellationToken);
                return new BrandRefresh(brandId, RefreshOutcomes.Skipped, null);
            }

            var import = await scope.ServiceProvider.GetRequiredService<FeedRefresher>().RefreshAsync(url, null, cancellationToken);
            await database.CommitAsync(cancellationToken);
            return import is null
                ? new BrandRefresh(brandId, RefreshOutcomes.Skipped, null)
                : new BrandRefresh(brandId, import.Status, import.Id);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The catalogue refresh failed for brand {BrandId}.", brandId);
            return new BrandRefresh(brandId, RefreshOutcomes.Error, null);
        }
    }
}
