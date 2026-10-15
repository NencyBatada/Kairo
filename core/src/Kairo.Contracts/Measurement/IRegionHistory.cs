namespace Kairo.Contracts.Measurement;

public enum RegionUnit
{
    City,
    State,
}

public interface IRegionHistory
{
    Task<RegionHistory> DailyAsync(Guid brandId, RegionUnit unit, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record RegionHistory(RegionUnit Unit, DateOnly From, DateOnly To, bool HasSales, string? Currency, IReadOnlyList<RegionSeries> Regions)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

public sealed record RegionSeries(
    string Id,
    string Name,
    string? GeoTargetId,
    string? State,
    double[]? Orders,
    double[]? Conversions,
    double[]? Revenue);
