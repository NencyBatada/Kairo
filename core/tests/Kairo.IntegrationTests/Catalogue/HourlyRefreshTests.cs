using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class HourlyRefreshTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string FirstFeed = "https://one.acme.example/feed.csv";
    private const string SecondFeed = "https://two.acme.example/feed.csv";

    private static readonly string OneProduct = Catalogues.Csv(Catalogues.Row("SKU-1"));
    private static readonly string TwoProducts = Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"));

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, _api.Clock.GetUtcNow().AddHours(1));
    }

    [Fact]
    public async Task A_run_refreshes_every_brand_with_a_feed()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        var fileAdmin = await Workspaces.SignInAsync(_api);
        var fileBrand = await Workspaces.CreateAsync(fileAdmin.Browser);
        await Catalogues.ImportAsync(fileAdmin.Browser, fileBrand, OneProduct);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync();

        Assert.Equal(2, run.GetProperty("brands").GetInt32());
        foreach (var brandId in new[] { first.BrandId, second.BrandId })
        {
            Assert.Equal("succeeded", OutcomeOf(run, brandId));
            var imports = await CatalogueRows.ImportsAsync(_database, brandId);
            Assert.Equal(2, imports.Count);
            Assert.Null(imports[1].StartedBy);
            Assert.Equal(2, (await CatalogueRows.ProductsAsync(_database, brandId)).Count);
        }

        Assert.Single(await CatalogueRows.ImportsAsync(_database, fileBrand));
    }

    [Fact]
    public async Task A_run_for_one_brand_refreshes_only_that_brand()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync(first.BrandId);

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        Assert.Equal(2, (await CatalogueRows.ImportsAsync(_database, first.BrandId)).Count);
        Assert.Single(await CatalogueRows.ImportsAsync(_database, second.BrandId));
    }

    [Fact]
    public async Task One_brands_failure_doesnt_stop_the_others()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync();

        Assert.Equal("failed", OutcomeOf(run, first.BrandId));
        Assert.Equal("succeeded", OutcomeOf(run, second.BrandId));
        Assert.Equal("bad_status", (await LatestImportAsync(first)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_brand_already_importing_is_skipped()
    {
        var brand = await BrandWithFeedAsync(FirstFeed);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);

        JsonElement run;
        await using (await CatalogueLocks.HoldAsync(_database, brand.BrandId))
        {
            run = await RunAsync();
        }

        Assert.Equal("skipped", OutcomeOf(run, brand.BrandId));
        Assert.Single(await CatalogueRows.ImportsAsync(_database, brand.BrandId));
    }

    [Fact]
    public async Task Three_failed_runs_in_a_row_alert_the_admins_once_until_a_refresh_succeeds()
    {
        var brand = await BrandWithFeedAsync(FirstFeed);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("failed", OutcomeOf(await RunAsync(), brand.BrandId));
        }

        var checkedAt = _worker.Clock.GetUtcNow();
        var afterFailures = await SourceAsync(brand);
        var alertsAfterFailures = await AlertsAsync(brand.BrandId);
        _worker.Websites.Text(FirstFeed, "text/csv", OneProduct);
        var recovered = OutcomeOf(await RunAsync(), brand.BrandId);
        var afterSuccess = await SourceAsync(brand);
        _worker.Websites.Status(FirstFeed, HttpStatusCode.InternalServerError);
        for (var i = 0; i < 3; i++)
        {
            await RunAsync();
        }

        Assert.Equal(4, afterFailures.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(checkedAt, afterFailures.GetProperty("lastCheckedAt").GetDateTimeOffset());
        var alert = Assert.Single(alertsAfterFailures);
        Assert.Equal(brand.Admin.UserId, alert.UserId);
        Assert.Equal("Your product feed isn't refreshing", alert.Title);
        Assert.Equal("Kairo couldn't refresh your product feed 3 times in a row.", alert.Body);
        Assert.Equal($"/{brand.Handle}/brand-kit/catalogue", alert.Link);
        Assert.Equal("unchanged", recovered);
        Assert.Equal(0, afterSuccess.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(2, (await AlertsAsync(brand.BrandId)).Count);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static string OutcomeOf(JsonElement run, Guid brandId) =>
        run.GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("brandId").GetGuid() == brandId)
            .GetProperty("outcome").GetString()!;

    private async Task<JsonElement> RunAsync(Guid? brandId = null)
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await _worker.RefreshCatalogueAsync(brandId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<List<NotificationRow>> AlertsAsync(Guid brandId) =>
        (await NotificationRows.NotificationsAsync(_database, brandId)).Where(row => row.Kind == "connection.problem").ToList();

    private static async Task<JsonElement> SourceAsync((Member Admin, Guid BrandId, string Handle) brand) =>
        await brand.Admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brand.BrandId)}/source", Token);

    private static async Task<JsonElement> LatestImportAsync((Member Admin, Guid BrandId, string Handle) brand) =>
        (await brand.Admin.Browser.GetFromJsonAsync<JsonElement>(Catalogues.Imports(brand.BrandId), Token))[0];

    private async Task<(Member Admin, Guid BrandId, string Handle)> BrandWithFeedAsync(string url)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        _api.Websites.Text(url, "text/csv", OneProduct);
        var response = await admin.Browser.PutAsJsonAsync($"{Catalogues.Base(brandId)}/feed", new { url }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (admin, brandId, handle);
    }
}
