using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class FeedStatusTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => _api.Clock.GetUtcNow();

    [Fact]
    public async Task A_new_workspace_has_no_feed_set_up()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var status = await StatusAsync(admin, brandId);

        Assert.Equal(AnalyticsOptions.DatasetFor(brandId), status.GetProperty("dataset").GetString());
        var feeds = status.GetProperty("feeds").EnumerateArray().ToList();
        Assert.Equal(new[] { "google-ads", "ga4", "sales-table" }, feeds.Select(feed => feed.GetProperty("feed").GetString()));
        Assert.All(feeds, feed =>
        {
            Assert.Equal("not_set_up", feed.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastRunAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, feed.GetProperty("coveredThrough").ValueKind);
        });
    }

    [Fact]
    public async Task A_connected_feed_waits_for_its_first_run()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();

        Assert.Equal("waiting", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_skipped_latest_run_still_counts_as_waiting()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "skipped", Now.AddHours(-1));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("waiting", feed.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, feed.GetProperty("lastRunAt").ValueKind);
    }

    [Fact]
    public async Task A_connection_that_needs_attention_shows_on_its_feed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        (await admin.Browser.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        Assert.Equal("needs_connection", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_succeeded_run_is_ok_and_says_how_far_the_data_reaches()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        var started = Now.AddHours(-1);
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "succeeded", started, new DateOnly(2026, 10, 12));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("ok", feed.GetProperty("state").GetString());
        Assert.Equal("2026-10-12", feed.GetProperty("coveredThrough").GetString());
        Assert.Equal(started.AddMinutes(1), feed.GetProperty("lastSuccessAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastError").ValueKind);
    }

    [Fact]
    public async Task A_run_in_progress_shows_as_running()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "running", Now.AddMinutes(-30));

        Assert.Equal("running", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_run_that_never_finished_shows_as_failing()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "running", Now.AddHours(-3));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("failing", feed.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastError").ValueKind);
    }

    [Fact]
    public async Task A_failed_run_is_failing_and_keeps_the_last_good_day()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "succeeded", Now.AddDays(-2), new DateOnly(2026, 10, 10));
        await FeedRunRows.InsertAsync(
            sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "failed", Now.AddHours(-1), error: "Google answered 500: boom");

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("failing", feed.GetProperty("state").GetString());
        Assert.Equal("Google answered 500: boom", feed.GetProperty("lastError").GetString());
        Assert.Equal("2026-10-10", feed.GetProperty("coveredThrough").GetString());
    }

    [Fact]
    public async Task The_sales_table_feed_needs_a_table()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");

        Assert.Equal("not_set_up", (await FeedAsync(admin, brandId, "sales-table")).GetProperty("state").GetString());

        (await admin.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/analytics/sales/source",
            new
            {
                table = "acme-data.shop.daily_orders",
                columns = new { date = "order_date", region = "state", orders = "order_count", revenue = "net_revenue" },
            },
            Token)).EnsureSuccessStatusCode();

        Assert.Equal("waiting", (await FeedAsync(admin, brandId, "sales-table")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Another_workspace_cannot_read_the_feeds()
    {
        var (_, brandId) = await WorkspaceAsync();
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync($"/api/brands/{brandId}/analytics/feeds", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static async Task<JsonElement> StatusAsync(Member admin, Guid brandId) =>
        await admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/analytics/feeds", Token);

    private static async Task<JsonElement> FeedAsync(Member admin, Guid brandId, string feed) =>
        (await StatusAsync(admin, brandId)).GetProperty("feeds").EnumerateArray().Single(entry => entry.GetProperty("feed").GetString() == feed);

    private static Task<JsonElement> AdsFeedAsync(Member admin, Guid brandId) => FeedAsync(admin, brandId, "google-ads");

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<(Member Admin, Guid BrandId)> AdsWorkspaceAsync()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        return (admin, brandId);
    }
}
