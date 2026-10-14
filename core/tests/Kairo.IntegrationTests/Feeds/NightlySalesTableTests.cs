using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlySalesTableTests(SqlServer sqlServer) : IAsyncLifetime
{
    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _api.GoogleApis.BigQuery;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_copies_the_brands_sales_table()
    {
        var (_, brandId) = await BrandWithSalesTableAsync();
        BigQuery.SalesTableRows.AddRange(
        [
            new SalesTableRow("2026-10-05", "Goa", "4", "400.50"),
            new SalesTableRow("2026-10-06", "Goa", "2", "200"),
            new SalesTableRow("2026-10-06", "Kerala", "1", "10"),
            new SalesTableRow("2025-01-01", "Goa", "9", "9"),
        ]);

        var run = await RunAsync();

        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(3, outcome.GetProperty("rows").GetInt32());
        Assert.Equal(
            new[] { "2026-10-05 Goa 4 400.50 table", "2026-10-06 Goa 2 200 table", "2026-10-06 Kerala 1 10 table" },
            SalesFiles.RowsOf(BigQuery, brandId));
        var query = BigQuery.Queries.First(query => query.Project == FakeBigQuery.BrandProject);
        Assert.Equal(14, BigQuery.Queries.Count(query => query.Project == FakeBigQuery.BrandProject));
        Assert.StartsWith("access-", query.AccessToken);
        Assert.Equal("asia-south1", query.Location);
        Assert.Contains("FROM `acme-data.shop.daily_orders`", query.Sql);
        Assert.Contains("CAST(SUM(`order_count`) AS INT64) AS orders", query.Sql);
        Assert.Contains("CAST(SUM(`net_revenue`) AS NUMERIC) AS revenue", query.Sql);
        Assert.Contains("WHERE DATE(`order_date`) BETWEEN @from AND @to", query.Sql);
    }

    [Fact]
    public async Task A_timestamp_column_is_counted_by_the_day_in_India()
    {
        await BrandWithSalesTableAsync("acme-data.shop.orders", "ordered_at");

        await RunAsync();

        var query = BigQuery.Queries.First(query => query.Project == FakeBigQuery.BrandProject);
        Assert.Contains("SELECT DATE(`ordered_at`, 'Asia/Kolkata') AS date", query.Sql);
        Assert.Contains("WHERE DATE(`ordered_at`, 'Asia/Kolkata') BETWEEN @from AND @to", query.Sql);
    }

    [Fact]
    public async Task The_sales_window_is_in_India_time()
    {
        await BrandWithSalesTableAsync();

        await RunAsync();

        var queries = BigQuery.Queries.Where(query => query.Project == FakeBigQuery.BrandProject).ToList();
        Assert.Equal("2025-09-14", ParameterValue(queries[0].Parameters, "from"));
        Assert.Equal("2025-09-30", ParameterValue(queries[0].Parameters, "to"));
        Assert.Equal("2026-10-01", ParameterValue(queries[^1].Parameters, "from"));
        Assert.Equal("2026-10-13", ParameterValue(queries[^1].Parameters, "to"));
    }

    [Fact]
    public async Task Without_a_sales_table_nothing_is_recorded()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");

        var run = await RunAsync();

        Assert.DoesNotContain(run.GetProperty("runs").EnumerateArray(), outcome => outcome.GetProperty("feed").GetString() == FeedNames.SalesTable);
        Assert.DoesNotContain(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.SalesTable);
    }

    [Fact]
    public async Task A_sales_table_without_the_BigQuery_connection_is_skipped()
    {
        var (admin, brandId) = await BrandWithSalesTableAsync();
        (await admin.Browser.DeleteAsync($"/api/brands/{brandId}/connections/bigquery", Token)).EnsureSuccessStatusCode();

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        var stored = Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.SalesTable);
        Assert.Equal("not_connected", stored.Reason);
    }

    [Fact]
    public async Task The_later_of_an_upload_and_a_table_copy_wins_each_day()
    {
        var (admin, brandId) = await BrandWithSalesTableAsync();
        BigQuery.SalesTableRows.Add(new SalesTableRow("2026-10-06", "Goa", "2", "200"));
        var upload = SalesFiles.Header + "2026-10-06,Punjab,1,50\n2026-10-06,Goa,50,5000\n";
        (await admin.Browser.PostAsync($"/api/brands/{brandId}/analytics/sales/uploads", SalesFiles.Csv(upload), Token)).EnsureSuccessStatusCode();

        await RunAsync();

        Assert.Equal(new[] { "2026-10-06 Goa 2 200 table" }, SalesFiles.RowsOf(BigQuery, brandId));

        (await admin.Browser.PostAsync($"/api/brands/{brandId}/analytics/sales/uploads", SalesFiles.Csv(upload), Token)).EnsureSuccessStatusCode();

        Assert.Equal(new[] { "2026-10-06 Goa 50 5000 upload", "2026-10-06 Punjab 1 50 upload" }, SalesFiles.RowsOf(BigQuery, brandId));
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<JsonElement> RunAsync()
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await _worker.RunDailyAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> BrandWithSalesTableAsync(
        string table = "acme-data.shop.daily_orders",
        string date = "order_date")
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");
        (await admin.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/analytics/sales/source",
            new
            {
                table,
                columns = new { date, region = "state", orders = "order_count", revenue = "net_revenue" },
            },
            Token)).EnsureSuccessStatusCode();
        return (admin, brandId);
    }

    private static string? ParameterValue(JsonElement parameters, string name) =>
        parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("parameterValue").GetProperty("value").GetString();

    private static JsonElement Outcome(JsonElement run, Guid brandId) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == FeedNames.SalesTable);
}
