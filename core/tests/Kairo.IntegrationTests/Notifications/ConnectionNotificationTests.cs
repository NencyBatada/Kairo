using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Notifications;

public sealed class ConnectionNotificationTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rejected_token_tells_every_admin_and_no_one_else()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var issued = await Connections.ConnectAsync(_api, owner.Browser, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);

        (await owner.Browser.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        var notifications = await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(new[] { owner.UserId, admin.UserId }.Order(), notifications.Select(row => row.UserId).Order());
        Assert.All(notifications, row =>
        {
            Assert.Equal(("connection.problem", "Google Ads needs reconnecting", "/settings/connections"), (row.Kind, row.Title, row.Link));
            Assert.Equal("Google stopped accepting Kairo's access to Google Ads. Reconnect it so refreshes and changes can continue.", row.Body);
        });
        Assert.Equal(2, (await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId)).Count);
    }

    [Fact]
    public async Task An_unreadable_token_tells_the_admins_Kairo_can_no_longer_read_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, first, "google-ads");
        await Connections.ConnectAsync(_api, owner.Browser, second, "google-ads");
        await Connections.CopyStoredTokenAsync(sqlServer.KairoConnectionString, first, second, "google-ads");

        (await owner.Browser.PostAsync($"/api/brands/{second}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, second));
        Assert.Equal("Kairo can no longer read its stored access to Google Ads. Reconnect it so refreshes and changes can continue.", notification.Body);
        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, first));
    }

    [Fact]
    public async Task An_admin_who_loses_the_role_leaves_the_other_admins_a_notice_and_gets_none()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "ga4");

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal(owner.UserId, notification.UserId);
        Assert.Equal("Google Analytics needs reconnecting", notification.Title);
        Assert.Equal(
            "Google Analytics was connected by an admin who has left or is no longer an admin. Reconnect it so refreshes and changes can continue.",
            notification.Body);
    }

    [Fact]
    public async Task An_admin_who_is_removed_leaves_the_other_admins_a_notice_and_gets_none()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "youtube");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal((owner.UserId, "YouTube needs reconnecting"), (notification.UserId, notification.Title));
    }

    [Fact]
    public async Task Disconnecting_on_purpose_sends_no_notice()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "merchant-center");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/connections/merchant-center", Token)).EnsureSuccessStatusCode();

        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
