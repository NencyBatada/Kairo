namespace Kairo.Contracts.Opportunities;

public interface IKeywordDemand
{
    Task<IReadOnlyList<RegionSearches>> MonthlyAsync(IReadOnlyList<string> keywords, IReadOnlyList<string> geoTargetIds, CancellationToken cancellationToken);
}

public sealed record RegionSearches(string GeoTargetId, IReadOnlyList<MonthSearches> Months);

public sealed record MonthSearches(int Year, int Month, long Searches);
