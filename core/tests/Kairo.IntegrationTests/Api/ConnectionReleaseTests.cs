using System.Net.Http.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionReleaseTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_an_admin_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task An_admin_who_leaves_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await other.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task Losing_the_admin_role_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task Another_admins_connections_are_untouched()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "youtube");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Equal("connected", (await Connections.GetAsync(owner.Browser, brandId, "youtube")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_departing_admins_Google_account_stays_granted_while_another_workspace_uses_it()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var elsewhere = await Workspaces.CreateAsync(other.Browser);
        await Connections.ConnectAsync(_api, other.Browser, elsewhere, "youtube", subject: "agency-account");
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads", subject: "agency-account");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.DoesNotContain(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal("connected", (await Connections.GetAsync(other.Browser, elsewhere, "youtube")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Removing_an_admin_works_when_their_token_cannot_be_decrypted()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");
        await Connections.SetKeyVersionAsync(sqlServer.KairoConnectionString, brandId, "google-ads", "projects/elsewhere/cryptoKeyVersions/1");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        var connection = await Connections.GetAsync(owner.Browser, brandId, "google-ads");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("Google Ads was connected by an admin who has left or is no longer an admin.", connection.GetProperty("reconnectReason").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Member Other, Guid BrandId)> TwoAdminsAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        return (owner, other, brandId);
    }

    private async Task AssertReleasedAsync(HttpClient owner, Guid brandId, IssuedCode issued)
    {
        var connection = await Connections.GetAsync(owner, brandId, "google-ads");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("Google Ads was connected by an admin who has left or is no longer an admin.", connection.GetProperty("reconnectReason").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
    }
}
