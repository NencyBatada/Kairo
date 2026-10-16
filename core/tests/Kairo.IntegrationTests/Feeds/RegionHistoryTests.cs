using Kairo.Contracts.Measurement;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Feeds;

public sealed class RegionHistoryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 3);

    private readonly Guid _brandId = Guid.NewGuid();
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _api.GoogleApis.BigQuery;

    public async ValueTask InitializeAsync() => _api = new KairoApiFactory(await IsolatedDatabase.CreateAsync(sqlServer));

    [Fact]
    public async Task City_history_joins_Ads_GA4_and_sales_by_name()
    {
        BigQuery.Selects["ads_city_daily"] =
        [
            ["2026-10-01", "1007785", "Mumbai", "Maharashtra", "2.5", "INR", "120.5"],
            ["2026-10-03", "1007785", "Mumbai", "Maharashtra", "1", "INR", "80"],
            ["2026-10-02", "1007788", "Pune", "Maharashtra", "4", "INR", "60"],
            ["2026-09-30", "1007788", "Pune", "Maharashtra", "9", "INR", "99"],
        ];
        BigQuery.Selects["ga4_city_daily"] =
        [
            ["2026-10-01", "Mumbai", "Maharashtra", "3", "4500.5", "INR"],
            ["2026-10-02", "Bengaluru", "Karnataka", "5", "7000", "INR"],
        ];
        BigQuery.Selects["sales_daily"] =
        [
            ["2026-10-01", "Bombay", "7", "9100"],
            ["2026-10-02", "Bangalore", "2", "1800"],
            ["2026-10-02", "Maharashtra", "40", "52000"],
            ["2026-10-03", "Nagpur", "1", "999"],
        ];

        var history = await ReadAsync(RegionUnit.City);

        Assert.True(history.HasSales);
        Assert.Equal("INR", history.Currency);
        Assert.Equal(3, history.Days);
        Assert.Equal(["bengaluru", "mumbai", "nagpur", "pune"], history.Regions.Select(region => region.Id));
        var mumbai = history.Regions.Single(region => region.Id == "mumbai");
        Assert.Equal("Mumbai", mumbai.Name);
        Assert.Equal("1007785", mumbai.GeoTargetId);
        Assert.Equal("Maharashtra", mumbai.State);
        Assert.Equal([7d, 0, 0], mumbai.Orders!);
        Assert.Equal([3d, 0, 0], mumbai.Conversions!);
        Assert.Equal([9100d, 0, 0], mumbai.Revenue!);
        Assert.Equal([120.5d, 0, 80], mumbai.Cost!);
        var pune = history.Regions.Single(region => region.Id == "pune");
        Assert.Null(pune.Orders);
        Assert.Equal([0d, 4, 0], pune.Conversions!);
        Assert.Null(pune.Revenue);
        Assert.Equal([0d, 60, 0], pune.Cost!);
        var bengaluru = history.Regions.Single(region => region.Id == "bengaluru");
        Assert.Equal([0d, 2, 0], bengaluru.Orders!);
        Assert.Null(bengaluru.GeoTargetId);
        Assert.Equal([0d, 1800, 0], bengaluru.Revenue!);
        Assert.Null(bengaluru.Cost);
    }

    [Fact]
    public async Task State_history_reads_the_state_tables()
    {
        BigQuery.Selects["ads_region_daily"] = [["2026-10-02", "21342", "Maharashtra", null, "6", "INR", "250"]];
        BigQuery.Selects["sales_daily"] = [["2026-10-02", "maharashtra", "40", "52000"]];

        var history = await ReadAsync(RegionUnit.State);

        var state = Assert.Single(history.Regions);
        Assert.Equal("maharashtra", state.Id);
        Assert.Equal("21342", state.GeoTargetId);
        Assert.Equal([0d, 40, 0], state.Orders!);
        Assert.Equal([0d, 6, 0], state.Conversions!);
        Assert.Equal([0d, 250, 0], state.Cost!);
        var queries = BigQuery.Queries.Select(query => query.Sql).ToList();
        Assert.Contains(queries, sql => sql.Contains($"`{FakeBigQuery.KairoProject}.{AnalyticsOptions.DatasetFor(_brandId)}.ads_region_daily`", StringComparison.Ordinal));
        Assert.Contains(queries, sql => sql.Contains(".ga4_region_daily`", StringComparison.Ordinal));
        Assert.All(BigQuery.Queries, query => Assert.Equal(2, query.Parameters.GetArrayLength()));
    }

    [Fact]
    public async Task Missing_tables_read_as_no_history()
    {
        var history = await ReadAsync(RegionUnit.City);

        Assert.Empty(history.Regions);
        Assert.False(history.HasSales);
        Assert.Equal(3, BigQuery.Queries.Count);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<RegionHistory> ReadAsync(RegionUnit unit)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRegionHistory>().DailyAsync(_brandId, unit, From, To, Token);
    }
}
