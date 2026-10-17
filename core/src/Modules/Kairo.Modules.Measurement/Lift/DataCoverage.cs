using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Lift;

internal sealed class DataCoverage(MeasurementDbContext db)
{
    public async Task<DateOnly?> ThroughAsync(string kpi, CancellationToken cancellationToken)
    {
        if (kpi == LiftKpis.Orders)
        {
            var table = await CoveredAsync(FeedNames.SalesTable, cancellationToken);
            var upload = await db.SalesUploads.MaxAsync(row => (DateOnly?)row.LastDate, cancellationToken);
            return Later(table, upload);
        }

        return await CoveredAsync(FeedNames.Ga4, cancellationToken) ?? await CoveredAsync(FeedNames.GoogleAds, cancellationToken);
    }

    public Task<DateOnly?> CoveredAsync(string feed, CancellationToken cancellationToken) =>
        db.FeedRuns
            .Where(run => run.Feed == feed && run.Status == RunStatuses.Succeeded)
            .MaxAsync(run => run.ToDate, cancellationToken);

    private static DateOnly? Later(DateOnly? left, DateOnly? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;
}

internal static class LiftKpis
{
    public const string Orders = "orders";
    public const string Conversions = "conversions";
}
