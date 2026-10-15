using Kairo.Contracts.Opportunities;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Opportunities.Refresh;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Opportunities;

public sealed class KeywordDemandTests(SqlServer sqlServer) : IAsyncLifetime
{
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeKeywordPlanner Planner => _api.GoogleApis.Keywords;

    public async ValueTask InitializeAsync() => _api = new KairoApiFactory(await IsolatedDatabase.CreateAsync(sqlServer));

    [Fact]
    public async Task Monthly_searches_are_summed_for_each_region()
    {
        var brand = await Radar.BrandAsync(_api, catalogue: false);
        Radar.PlanDemand(Planner);
        Planner.RegionLastMonth[FakeKeywordPlanner.Maharashtra] = 2.0;

        var found = await MonthlyAsync(brand.BrandId, ["rain boots", "Rain Boots", "waterproof shoes", " "], [FakeKeywordPlanner.Maharashtra, FakeKeywordPlanner.Kerala, "not-an-id"]);

        Assert.Equal([FakeKeywordPlanner.Maharashtra, FakeKeywordPlanner.Kerala], found.Select(region => region.GeoTargetId));
        var maharashtra = found[0].Months;
        Assert.Equal(24, maharashtra.Count);
        Assert.Equal(300, maharashtra[0].Searches);
        Assert.Equal(1400, maharashtra[^1].Searches);
        Assert.True(maharashtra.Zip(maharashtra.Skip(1)).All(pair => (pair.First.Year, pair.First.Month).CompareTo((pair.Second.Year, pair.Second.Month)) < 0));
        var history = Planner.Calls.Where(call => call.Method == "history").ToList();
        Assert.Equal(2, history.Count);
        Assert.All(history, call => Assert.Equal(2, call.Body.GetProperty("keywords").GetArrayLength()));
    }

    [Fact]
    public async Task A_region_Google_cannot_answer_is_left_out()
    {
        var brand = await Radar.BrandAsync(_api, catalogue: false);
        Radar.PlanDemand(Planner);
        Planner.FailingRegions.Add(FakeKeywordPlanner.Kerala);

        var found = await MonthlyAsync(brand.BrandId, ["rain boots"], [FakeKeywordPlanner.Maharashtra, FakeKeywordPlanner.Kerala]);

        Assert.Equal(FakeKeywordPlanner.Maharashtra, Assert.Single(found).GeoTargetId);
    }

    [Fact]
    public async Task Without_Google_Ads_there_is_no_demand()
    {
        var brand = await Radar.BrandAsync(_api, connectAds: false, catalogue: false);

        var found = await MonthlyAsync(brand.BrandId, ["rain boots"], [FakeKeywordPlanner.Maharashtra]);

        Assert.Empty(found);
        Assert.Empty(Planner.Calls);
    }

    [Fact]
    public async Task Regions_are_capped()
    {
        var brand = await Radar.BrandAsync(_api, catalogue: false);
        Radar.PlanDemand(Planner);

        await MonthlyAsync(brand.BrandId, ["rain boots"], Enumerable.Range(1, 30).Select(id => id.ToString()).ToList());

        Assert.Equal(KeywordDemandReader.MaxRegions, Planner.Calls.Count(call => call.Method == "history"));
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<IReadOnlyList<RegionSearches>> MonthlyAsync(Guid brandId, IReadOnlyList<string> keywords, IReadOnlyList<string> regions)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IKeywordDemand>().MonthlyAsync(keywords, regions, Token);
    }
}
