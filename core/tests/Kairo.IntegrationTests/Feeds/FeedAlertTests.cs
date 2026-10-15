using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class FeedAlertTests(SqlServer sqlServer) : IAsyncLifetime
{
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
    public async Task Losing_access_to_the_Google_Ads_account_flags_the_connection_and_tells_the_admins_once()
    {
        var (owner, brandId) = await BrandWithAdsAsync();
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        Google.AdsAccessErrors["1111111111"] = "USER_PERMISSION_DENIED";

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal("failed", Outcome(first, brandId).GetProperty("status").GetString());
        Assert.Equal("skipped", Outcome(second, brandId).GetProperty("status").GetString());
        var stored = (await FeedRunRows.ListAsync(_database, brandId)).Where(run => run.Feed == FeedNames.GoogleAds).ToList();
        Assert.Equal(new (string, string?)[] { ("failed", "access_lost"), ("skipped", "needs_reconnect") }, stored.Select(run => (run.Status, run.Reason)));
        var connection = await Connections.GetAsync(owner.Browser, brandId, "google-ads");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal(
            "The Google account that connected Google Ads no longer has access to the chosen account.",
            connection.GetProperty("reconnectReason").GetString());
        Assert.Null(await Connections.StoredTokenAsync(_database, brandId, "google-ads"));

        var notifications = await NotificationRows.NotificationsAsync(_database, brandId);
        Assert.Equal(new[] { owner.UserId, admin.UserId }.Order(), notifications.Select(row => row.UserId).Order());
        Assert.DoesNotContain(notifications, row => row.UserId == marketer.UserId);
        Assert.All(notifications, row =>
        {
            Assert.Equal(("connection.problem", "Google Ads needs reconnecting"), (row.Kind, row.Title));
            Assert.Equal(
                "The Google account that connected Google Ads no longer has access to the chosen account. Reconnect it so refreshes and changes can continue.",
                row.Body);
        });
        var actions = await Workspaces.AuditActionsAsync(owner.Browser, brandId);
        Assert.Contains("connection.needs_reconnect", actions);
        Assert.Contains("analytics.feed_failed", actions);
        Assert.Contains(await OutboxRows.ReadAsync(_database, brandId), row => row.Type == "connection.changed");
    }

    [Fact]
    public async Task Losing_access_to_the_Google_Analytics_property_flags_that_connection()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "ga4");
        Google.Ga4Denied = true;

        var run = await RunAsync();

        Assert.Equal("failed", Outcome(run, brandId, FeedNames.Ga4).GetProperty("status").GetString());
        Assert.Equal("needsReconnect", (await Connections.GetAsync(owner.Browser, brandId, "ga4")).GetProperty("state").GetString());
        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_database, brandId));
        Assert.Equal("Google Analytics needs reconnecting", notification.Title);
    }

    [Fact]
    public async Task A_failing_feed_tells_the_admins_when_it_starts_failing_and_not_on_every_rerun()
    {
        var (owner, brandId) = await BrandWithAdsAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        Google.AdsFailures.Add("1111111111");

        await RunAsync();
        await RunAsync();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(_database, brandId));
        Assert.Equal(owner.UserId, notification.UserId);
        Assert.NotEqual(marketer.UserId, notification.UserId);
        Assert.Equal(("connection.problem", "Google Ads data didn't refresh", "/settings/connections"), (notification.Kind, notification.Title, notification.Link));
        Assert.Equal(
            "Kairo couldn't refresh the Google Ads data, so reports may be out of date. It will try again in the next nightly refresh. "
                + "Details: Google answered 500: Internal error encountered.",
            notification.Body);
        Assert.Equal("connected", (await Connections.GetAsync(owner.Browser, brandId, "google-ads")).GetProperty("state").GetString());

        Google.AdsFailures.Clear();
        await RunAsync();
        Google.AdsFailures.Add("1111111111");
        await RunAsync();

        Assert.Equal(2, (await NotificationRows.NotificationsAsync(_database, brandId)).Count);
    }

    [Fact]
    public async Task Each_run_is_audited_as_Kairo_and_published_live()
    {
        var (owner, brandId) = await BrandWithAdsAsync();

        await RunAsync();

        var page = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=200", Token);
        var feedEntries = page.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString()!.StartsWith("analytics.feed_", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["analytics.feed_succeeded", "analytics.feed_started"], feedEntries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.All(feedEntries, entry =>
        {
            Assert.Equal(("system", "Kairo", "feed_run"), (entry.GetProperty("actorType").GetString(), entry.GetProperty("actorName").GetString(), entry.GetProperty("targetType").GetString()));
            Assert.Equal(FeedNames.GoogleAds, entry.GetProperty("after").GetProperty("feed").GetString());
        });
        Assert.Equal("succeeded", feedEntries[0].GetProperty("after").GetProperty("status").GetString());

        var events = (await OutboxRows.ReadAsync(_database, brandId)).Where(row => row.Type == "feeds.changed").ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("Everyone", events[0].AudienceKind);
        Assert.Equal(
            new[] { "running", "succeeded" },
            events.Select(row => JsonDocument.Parse(row.Payload).RootElement.GetProperty("status").GetString()));
        Assert.Empty(await NotificationRows.NotificationsAsync(_database, brandId));
    }

    [Fact]
    public async Task A_skipped_feed_is_not_audited()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await RunAsync();

        Assert.DoesNotContain(await Workspaces.AuditActionsAsync(owner.Browser, brandId), action => action.StartsWith("analytics.feed_", StringComparison.Ordinal));
        Assert.DoesNotContain(await OutboxRows.ReadAsync(_database, brandId), row => row.Type == "feeds.changed");
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

    private async Task<(Member Owner, Guid BrandId)> BrandWithAdsAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "google-ads");
        return (owner, brandId);
    }

    private static JsonElement Outcome(JsonElement run, Guid brandId, string feed = FeedNames.GoogleAds) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == feed);
}
