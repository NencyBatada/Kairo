using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionAccountTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> EveryProduct => new()
    {
        { "google-ads", "1111111111", "Acme Shoes India" },
        { "youtube", "UC-acme", "Channel UC-acme" },
        { "merchant-center", "555", "Acme Store" },
        { "ga4", "777", "Acme web (Acme)" },
        { "bigquery", "acme-data", "Acme data" },
    };

    [Theory]
    [MemberData(nameof(EveryProduct))]
    public async Task An_admin_connects_each_product_by_choosing_an_account(string product, string accountId, string accountName)
    {
        var (admin, brandId) = await ConsentedAsync(product);

        var accounts = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/{product}/accounts", Token);
        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/{product}/account", new { externalId = accountId }, Token);

        Assert.Contains(accounts.EnumerateArray(), account =>
            account.GetProperty("id").GetString() == accountId && account.GetProperty("name").GetString() == accountName);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var connection = await Connections.GetAsync(admin, brandId, product);
        Assert.Equal("connected", connection.GetProperty("state").GetString());
        Assert.Equal(accountId, connection.GetProperty("externalId").GetString());
        Assert.Equal(accountName, connection.GetProperty("displayName").GetString());
        Assert.Contains("connection.account_chosen", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task Google_Ads_lists_only_accounts_that_can_run_campaigns()
    {
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var accounts = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/google-ads/accounts", Token);

        Assert.Equal(["1111111111", "3333333333"], accounts.EnumerateArray().Select(account => account.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task An_account_the_Google_login_cannot_reach_is_refused()
    {
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = "2222222222" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, chosen.StatusCode);
        Assert.Equal("choosingAccount", (await Connections.GetAsync(admin, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("google-ads", "can't make changes to it")]
    [InlineData("merchant-center", "can't make changes to it")]
    [InlineData("ga4", "can't read that Google Analytics property")]
    [InlineData("bigquery-read", "can't read datasets")]
    [InlineData("bigquery-jobs", "can't run queries")]
    public async Task A_failing_test_call_keeps_the_connection_unfinished_and_says_why(string failure, string reason)
    {
        var product = failure.StartsWith("bigquery", StringComparison.Ordinal) ? "bigquery" : failure;
        switch (failure)
        {
            case "google-ads":
                _api.GoogleApis.AdsWrite = HttpStatusCode.Forbidden;
                break;
            case "merchant-center":
                _api.GoogleApis.MerchantAccessRights = ["READ_ONLY"];
                break;
            case "ga4":
                _api.GoogleApis.Ga4Read = HttpStatusCode.Forbidden;
                break;
            case "bigquery-read":
                _api.GoogleApis.BigQueryRead = HttpStatusCode.Forbidden;
                break;
            case "bigquery-jobs":
                _api.GoogleApis.BigQueryJobs = HttpStatusCode.Forbidden;
                break;
        }

        var (admin, brandId) = await ConsentedAsync(product);

        var chosen = await admin.PostAsJsonAsync(
            $"/api/brands/{brandId}/connections/{product}/account", new { externalId = Connections.DefaultAccount(product) }, Token);

        Assert.Equal(HttpStatusCode.Conflict, chosen.StatusCode);
        Assert.Contains(reason, (await chosen.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
        Assert.Equal("choosingAccount", (await Connections.GetAsync(admin, brandId, product)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_Google_Ads_validation_error_still_proves_write_access()
    {
        _api.GoogleApis.AdsWrite = HttpStatusCode.BadRequest;
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = "1111111111" }, Token);

        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
    }

    [Fact]
    public async Task Listing_accounts_again_reuses_the_access_token()
    {
        var (admin, brandId) = await ConsentedAsync("youtube");

        await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/youtube/accounts", Token);
        await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/youtube/accounts", Token);

        Assert.Equal(1, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_product_must_be_connected_before_choosing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var accounts = await admin.Browser.GetAsync($"/api/brands/{brandId}/connections/google-ads/accounts", Token);

        Assert.Equal(HttpStatusCode.Conflict, accounts.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(HttpClient Admin, Guid BrandId)> ConsentedAsync(string product)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, product));
        return (admin.Browser, brandId);
    }
}
