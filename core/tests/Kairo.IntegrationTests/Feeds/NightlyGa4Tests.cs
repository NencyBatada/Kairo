using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlyGa4Tests(SqlServer sqlServer) : IAsyncLifetime
{
    private const int FirstWindowRows = 396;

    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeGoogleApis Google => _api.GoogleApis;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_loads_GA4_totals_by_day_and_region()
    {
        var brandId = await BrandWithGa4Async();

        var run = await RunAsync();

        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(FirstWindowRows, outcome.GetProperty("rows").GetInt32());
        var rows = Google.BigQuery.Rows(AnalyticsOptions.DatasetFor(brandId), "ga4_region_daily");
        Assert.Equal(FirstWindowRows, rows.Count);
        var day = rows.Single(row => row["date"]!.GetValue<string>() == "2026-10-13");
        Assert.Equal("777", day["property_id"]!.GetValue<string>());
        Assert.Equal("India", day["country"]!.GetValue<string>());
        Assert.Equal("Maharashtra", day["region"]!.GetValue<string>());
        Assert.Equal("INR", day["currency"]!.GetValue<string>());
        Assert.Equal(120, day["sessions"]!.GetValue<long>());
        Assert.Equal(4d, day["key_events"]!.GetValue<double>());
        Assert.Equal(3, day["transactions"]!.GetValue<long>());
        Assert.Equal("5600.75", day["purchase_revenue"]!.GetValue<string>());
        Assert.Contains(rows, row => row["region"]!.GetValue<string>() == "(not set)" && row["date"]!.GetValue<string>() == "2025-09-14");
    }

    [Fact]
    public async Task Every_page_of_the_report_is_read()
    {
        await BrandWithGa4Async();

        await RunAsync();

        Assert.Equal(new[] { 0, 100, 200, 300 }, Google.Ga4Requests.Select(request => request.GetProperty("offset").GetInt32()));
        Assert.All(Google.Ga4Requests, request => Assert.Equal(Ga4Reports.PageSize, request.GetProperty("limit").GetInt32()));
    }

    [Fact]
    public async Task The_window_follows_the_propertys_time_zone()
    {
        Google.Ga4TimeZone = "America/Los_Angeles";
        await BrandWithGa4Async();

        await RunAsync();
        var first = Google.Ga4Requests.First().GetProperty("dateRanges")[0];
        await RunAsync();
        var later = Google.Ga4Requests.Last().GetProperty("dateRanges")[0];

        Assert.Equal("2025-09-13", first.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-12", first.GetProperty("endDate").GetString());
        Assert.Equal("2026-10-06", later.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-12", later.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task A_brand_without_GA4_is_recorded_as_skipped()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        Assert.Equal("not_connected", Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.Ga4).Reason);
        Assert.Empty(Google.Ga4Requests);
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

    private async Task<Guid> BrandWithGa4Async()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "ga4");
        return brandId;
    }

    private static JsonElement Outcome(JsonElement run, Guid brandId) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == FeedNames.Ga4);
}
