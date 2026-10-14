using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal static class SalesTableQuery
{
    public static string For(SalesSource source) => $"""
        SELECT {DayOf(source)} AS date, CAST(`{source.RegionColumn}` AS STRING) AS region,
               CAST(SUM(`{source.OrdersColumn}`) AS INT64) AS orders, CAST(SUM(`{source.RevenueColumn}`) AS NUMERIC) AS revenue
        FROM `{source.TableRef}`
        WHERE {DayOf(source)} BETWEEN @from AND @to
        GROUP BY 1, 2
        """;

    private static string DayOf(SalesSource source) => source.DateColumnType == "TIMESTAMP"
        ? $"DATE(`{source.DateColumn}`, '{FeedClock.IndiaTimeZone}')"
        : $"DATE(`{source.DateColumn}`)";
}

internal sealed class SalesTableFeed(MeasurementDbContext db, BigQueryClient bigQuery, BigQueryTables tables, TimeProvider time) : IFeed
{
    private SalesSource? _source;

    public string Name => FeedNames.SalesTable;

    public ConnectedProduct Product => ConnectedProduct.BigQuery;

    public async Task<bool> IsSetUpAsync(CancellationToken cancellationToken)
    {
        _source = await db.SalesSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return _source is not null;
    }

    public string TargetOf(ConnectionStatus status) => _source?.TableRef ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var source = _source ?? throw new InvalidOperationException("No sales table is set.");
        var window = FeedWindow.For(FeedClock.Today(time, FeedClock.IndiaTimeZone), firstRun);
        var loaded = 0;
        foreach (var month in window.Months())
        {
            var results = await bigQuery.QueryAsync(
                access.ExternalId,
                access.AccessToken,
                SalesTableQuery.For(source),
                source.Location,
                [QueryParameter.Date("from", month.From), QueryParameter.Date("to", month.To)],
                cancellationToken);
            var rows = results
                .Select(result => new JsonObject
                {
                    ["date"] = result[0],
                    ["region"] = result[1],
                    ["orders"] = long.Parse(result[2] ?? "0", CultureInfo.InvariantCulture),
                    ["revenue"] = result[3] ?? "0",
                    ["source"] = SalesRows.Table,
                })
                .ToList();
            loaded += await tables.ReplaceDaysAsync(brandId, TableSchemas.SalesDaily, month.Days(), rows, cancellationToken);
        }

        return new FeedResult(window, loaded);
    }
}
