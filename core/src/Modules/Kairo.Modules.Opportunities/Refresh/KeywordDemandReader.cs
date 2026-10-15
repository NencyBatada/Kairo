using Kairo.Contracts.Connections;
using Kairo.Contracts.Opportunities;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Opportunities.Refresh;

internal sealed class KeywordDemandReader(
    IConnections connections,
    KeywordPlanner planner,
    ILogger<KeywordDemandReader> logger) : IKeywordDemand
{
    public const int MaxKeywords = 200;
    public const int MaxRegions = 20;

    public async Task<IReadOnlyList<RegionSearches>> MonthlyAsync(
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> geoTargetIds,
        CancellationToken cancellationToken)
    {
        var wanted = keywords.Select(keyword => keyword.Trim()).Where(keyword => keyword.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxKeywords).ToList();
        var regions = geoTargetIds.Where(id => id.Length > 0 && id.All(char.IsAsciiDigit)).Distinct().Take(MaxRegions).ToList();
        if (wanted.Count == 0 || regions.Count == 0
            || (await connections.GetStatusAsync(ConnectedProduct.GoogleAds, cancellationToken)).State != ConnectionState.Connected)
        {
            return [];
        }

        var access = await connections.GetAccessAsync(ConnectedProduct.GoogleAds, cancellationToken);
        var found = new Dictionary<string, RegionSearches>();
        await Parallel.ForEachAsync(
            regions,
            new ParallelOptions { MaxDegreeOfParallelism = BrandRefresher.RegionsAtOnce, CancellationToken = cancellationToken },
            async (region, token) =>
            {
                try
                {
                    var history = await planner.HistoryAsync(access, new GeoTarget(region, "", ""), wanted, token);
                    var months = history
                        .SelectMany(keyword => keyword.Months)
                        .GroupBy(month => (month.Year, month.Month))
                        .OrderBy(month => month.Key)
                        .Select(month => new MonthSearches(month.Key.Year, month.Key.Month, month.Sum(entry => entry.Searches)))
                        .ToList();
                    lock (found)
                    {
                        found[region] = new RegionSearches(region, months);
                    }
                }
                catch (AdsCallException exception) when (!exception.AccessLost)
                {
                    logger.LogWarning(exception, "Google Ads had no search demand for geo target {Region}.", region);
                }
            });

        return regions.Where(found.ContainsKey).Select(region => found[region]).ToList();
    }
}
