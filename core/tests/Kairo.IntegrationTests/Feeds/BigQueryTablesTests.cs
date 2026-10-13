using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Feeds;

public sealed class BigQueryTablesTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly DateOnly First = new(2026, 10, 1);
    private static readonly DateOnly Second = new(2026, 10, 2);
    private static readonly DateOnly Third = new(2026, 10, 3);

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);
    private readonly Guid _brandId = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _api.GoogleApis.BigQuery;

    private string Dataset => AnalyticsOptions.DatasetFor(_brandId);

    [Fact]
    public async Task The_first_write_creates_the_dataset_and_a_day_partitioned_table_once()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        await ReplaceAsync([Second], Sale(Second, "Goa", 2));

        Assert.Equal(1, BigQuery.Count($"datasets.insert {Dataset}"));
        Assert.Equal(1, BigQuery.Count($"tables.insert {Dataset}.sales_daily"));
        Assert.Equal("asia-south1", BigQuery.DatasetLocation(Dataset));
        var table = BigQuery.TableInserts.Single(insert => insert["tableReference"]!["datasetId"]!.GetValue<string>() == Dataset);
        Assert.Equal("DAY", table["timePartitioning"]!["type"]!.GetValue<string>());
        Assert.Equal("date", table["timePartitioning"]!["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task Each_row_carries_the_time_it_was_loaded()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));

        var row = Assert.Single(BigQuery.Rows(Dataset, "sales_daily"));
        var loadedAt = DateTimeOffset.Parse(row["loaded_at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        Assert.True(Math.Abs((loadedAt - _api.Clock.GetUtcNow()).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task Running_the_same_load_twice_gives_the_same_rows()
    {
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));

        Assert.Equal(new[] { 1L, 2L }, Orders());
    }

    [Fact]
    public async Task A_day_that_now_has_no_rows_is_emptied()
    {
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));
        await ReplaceAsync([First, Second], Sale(First, "Goa", 5));

        var row = Assert.Single(BigQuery.Rows(Dataset, "sales_daily"));
        Assert.Equal("2026-10-01", row["date"]!.GetValue<string>());
        Assert.Equal(5, row["orders"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_load_with_no_rows_only_deletes()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        var loads = BigQuery.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal));

        await ReplaceAsync([First]);

        Assert.Empty(BigQuery.Rows(Dataset, "sales_daily"));
        Assert.Equal(loads, BigQuery.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal)));
        Assert.Equal($"DELETE FROM `{FakeBigQuery.KairoProject}.{Dataset}.sales_daily` WHERE date IN UNNEST(@dates);", BigQuery.Queries.Last().Sql);
    }

    [Fact]
    public async Task Days_that_are_not_being_loaded_are_untouched()
    {
        await ReplaceAsync([First, Second, Third], Sale(First, "Goa", 1), Sale(Second, "Goa", 2), Sale(Third, "Goa", 3));
        await ReplaceAsync([Second], Sale(Second, "Goa", 20));

        Assert.Equal(new[] { 1L, 20L, 3L }, Orders());
    }

    [Fact]
    public async Task A_failed_replace_keeps_the_rows_and_removes_the_staging_table()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        BigQuery.FailScripts = true;

        var failure = await Assert.ThrowsAsync<GoogleApiException>(() => ReplaceAsync([First], Sale(First, "Goa", 9)));

        Assert.Equal("Google answered 400: Quota exceeded", failure.Message);
        Assert.Equal(new[] { 1L }, Orders());
        Assert.DoesNotContain(BigQuery.TableNames(Dataset), table => table.StartsWith("_staging_", StringComparison.Ordinal));
        Assert.Contains(BigQuery.Calls, call => call.StartsWith($"tables.delete {Dataset}._staging_sales_daily_", StringComparison.Ordinal));
    }

    [Fact]
    public void The_script_deletes_the_days_and_inserts_the_staged_rows_in_one_transaction()
    {
        var script = ReplaceScript.For("p", "d", TableSchemas.SalesDaily, "_staging_sales_daily_1");

        var expected = """
            BEGIN TRANSACTION;
            DELETE FROM `p.d.sales_daily` WHERE date IN UNNEST(@dates);
            INSERT INTO `p.d.sales_daily` (date, region, orders, revenue, source, loaded_at) SELECT date, region, orders, revenue, source, loaded_at FROM `p.d._staging_sales_daily_1`;
            COMMIT TRANSACTION;
            DROP TABLE `p.d._staging_sales_daily_1`;
            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), script.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_tables_have_the_documented_columns()
    {
        Assert.Equal(
            "date DATE, customer_id STRING, campaign_id STRING, region_id STRING, region_name STRING, country_code STRING, currency STRING, "
            + "impressions INT64, clicks INT64, cost NUMERIC, conversions FLOAT64, conversions_value NUMERIC, loaded_at TIMESTAMP",
            string.Join(", ", TableSchemas.AdsRegionDaily.Columns.Select(column => $"{column.Name} {column.Type}")));
        Assert.Equal(
            "date DATE, property_id STRING, country STRING, region STRING, currency STRING, sessions INT64, key_events FLOAT64, "
            + "transactions INT64, purchase_revenue NUMERIC, loaded_at TIMESTAMP",
            string.Join(", ", TableSchemas.Ga4RegionDaily.Columns.Select(column => $"{column.Name} {column.Type}")));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private Task<int> ReplaceAsync(IReadOnlyCollection<DateOnly> dates, params JsonObject[] rows) =>
        _api.Services.GetRequiredService<BigQueryTables>().ReplaceDaysAsync(_brandId, TableSchemas.SalesDaily, dates, rows, Token);

    private long[] Orders() => BigQuery.Rows(Dataset, "sales_daily")
        .OrderBy(row => row["date"]!.GetValue<string>(), StringComparer.Ordinal)
        .Select(row => row["orders"]!.GetValue<long>())
        .ToArray();

    private static JsonObject Sale(DateOnly date, string region, long orders) => new()
    {
        ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["region"] = region,
        ["orders"] = orders,
        ["revenue"] = "10.00",
        ["source"] = "upload",
    };
}
