using Kairo.IntegrationTests.Api;
using Kairo.Modules.BrandKit.Drafting;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BestAdsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_the_best_ads_of_the_last_90_days_from_Google_Ads()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.BestAdRows =
        [
            FakeGoogleApis.SearchAd("7001", ["Run further", "Free returns", "Run further"], ["Shoes for Indian roads."]),
            FakeGoogleApis.DisplayAd("7002", ["Diwali sale"], ["Up to 40% off."]),
        ];

        var ads = await ReadAsync(brandId);

        Assert.Equal(new[] { "7001", "7002" }, ads.Select(ad => ad.Id));
        Assert.Equal(new[] { "Run further", "Free returns" }, ads[0].Headlines);
        Assert.Equal(new[] { "Up to 40% off." }, ads[1].Descriptions);
        var report = Assert.Single(_api.GoogleApis.AdsReports, report => report.Query.Contains("LAST_90_DAYS", StringComparison.Ordinal));
        Assert.Equal("1111111111", report.Customer);
        Assert.Equal(BestAds.Query, report.Query);
    }

    [Fact]
    public async Task Without_a_Google_Ads_connection_there_are_no_ads()
    {
        var (_, brandId) = await WorkspaceAsync();

        var ads = await ReadAsync(brandId);

        Assert.Empty(ads);
        Assert.Empty(_api.GoogleApis.AdsReports);
    }

    [Fact]
    public async Task A_Google_Ads_error_means_no_ads_rather_than_a_failure()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.AdsFailures.Add("1111111111");

        Assert.Empty(await ReadAsync(brandId));
    }

    [Fact]
    public void Ads_without_text_or_a_numeric_ID_are_skipped()
    {
        var ads = BestAds.Parse("""
            [{"results":[
              {"adGroupAd":{"ad":{"id":"7001","responsiveSearchAd":{"headlines":[{"text":" Run "}],"descriptions":[]}}}},
              {"adGroupAd":{"ad":{"id":"7002","responsiveSearchAd":{"headlines":[],"descriptions":[]}}}},
              {"adGroupAd":{"ad":{"id":"x7","responsiveSearchAd":{"headlines":[{"text":"Bad"}]}}}},
              {"adGroupAd":{"ad":{"id":"7001","responsiveSearchAd":{"headlines":[{"text":"Again"}]}}}},
              {"adGroupAd":{"ad":{"id":"7003","responsiveDisplayAd":{"headlines":[{"text":"Sale"}],"descriptions":[{"text":"40% off"}]}}}}
            ]}]
            """);

        Assert.Equal(new[] { "7001", "7003" }, ads.Select(ad => ad.Id));
        Assert.Equal(new[] { "Run" }, ads[0].Headlines);
        Assert.Equal(new[] { "40% off" }, ads[1].Descriptions);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<IReadOnlyList<BestAd>> ReadAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<BestAds>().ReadAsync(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
