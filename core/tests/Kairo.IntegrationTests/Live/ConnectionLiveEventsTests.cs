using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class ConnectionLiveEventsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Giving_consent_and_choosing_an_account_each_publish_connection_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await Connections.ConnectAsync(_api, owner.Browser, brandId, "google-ads");

        await AssertConnectionChangedAsync(listener, "google-ads");
        await AssertConnectionChangedAsync(listener, "google-ads");
    }

    [Fact]
    public async Task Disconnecting_publishes_connection_changed_to_every_member()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "youtube");
        await using var listener = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/connections/youtube", Token)).EnsureSuccessStatusCode();

        await AssertConnectionChangedAsync(listener, "youtube");
    }

    [Fact]
    public async Task A_rejected_token_publishes_connection_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var issued = await Connections.ConnectAsync(_api, owner.Browser, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        await AssertConnectionChangedAsync(listener, "google-ads");
    }

    [Fact]
    public async Task A_departing_admins_connections_publish_connection_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "ga4");
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        var types = new[] { await listener.NextAsync(), await listener.NextAsync(), await listener.NextAsync() };
        Assert.Contains(types, message => message.Type == "team.changed");
        Assert.Contains(types, message => message.Type == "connection.changed" && message.Data.GetProperty("product").GetString() == "ga4");
        Assert.Contains(types, message => message.Type == "notification.created" && message.Data.GetProperty("kind").GetString() == "connection.problem");
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static async Task AssertConnectionChangedAsync(LiveListener listener, string product)
    {
        var message = await listener.NextAsync();
        Assert.Equal("connection.changed", message.Type);
        Assert.Equal(product, message.Data.GetProperty("product").GetString());
    }
}
