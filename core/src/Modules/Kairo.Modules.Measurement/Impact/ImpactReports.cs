using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement.Impact;

internal sealed class ImpactReports(MeasurementDbContext db) : IImpactReports
{
    public async Task<IReadOnlyList<ImpactSummary>> ListReadyAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var reports = await db.ImpactReports.AsNoTracking()
            .Where(row => row.Status == ImpactStatuses.Ready && row.ReadyAt >= since).OrderBy(row => row.ReadyAt).ToListAsync(cancellationToken);
        var ids = reports.Select(row => row.ReadingId).ToList();
        var readings = await db.LiftReadings.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        return [.. reports.Where(report => readings.ContainsKey(report.ReadingId)).Select(report => Summary(report, readings[report.ReadingId]))];
    }

    public async Task<ImpactSummary?> GetAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var report = await db.ImpactReports.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == roomId && row.Status == ImpactStatuses.Ready, cancellationToken);
        if (report is null) return null;
        var reading = await db.LiftReadings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == report.ReadingId, cancellationToken);
        return reading is null ? null : Summary(report, reading);
    }

    private static ImpactSummary Summary(ImpactReport report, LiftReading reading) => new(
        report.Id, report.BrandId, report.RoomId, report.Title, report.Kpi, report.Currency, reading.StartDate, reading.ThroughDate, report.Outcome,
        report.NextStep, reading.Lift, reading.Low, reading.High, reading.Extra, reading.ExtraLow, reading.ExtraHigh, report.Spend, report.Results,
        report.CostPerExtra, report.Summary ?? "", ImpactEndpoints.Read<ImpactFlag[]>(report.Flags) ?? [],
        [.. (ImpactEndpoints.Read<RegionResult[]>(reading.Regions) ?? []).Select(region => new ImpactRegion(region.Id, region.Name, region.Test,
            region.Status, region.Figures?.Conclusive ?? false, region.Figures?.Lift, region.Figures?.Low, region.Figures?.High, region.Figures?.Extra,
            region.Cost))],
        report.ReadyAt ?? report.UpdatedAt);
}
