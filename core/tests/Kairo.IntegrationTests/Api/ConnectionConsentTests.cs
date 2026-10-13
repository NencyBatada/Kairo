using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionConsentTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_lists_every_product_as_not_connected()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var list = await admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections", Token);

        Assert.Equal(
            ["google-ads", "youtube", "merchant-center", "ga4", "bigquery"],
            list.EnumerateArray().Select(connection => connection.GetProperty("product").GetString()!));
        Assert.All(list.EnumerateArray(), connection => Assert.Equal("notConnected", connection.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Connecting_asks_Google_for_offline_access_to_only_that_products_scopes()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var authorize = await Connections.StartAsync(admin.Browser, brandId, "youtube");

        var query = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal(FakeGoogle.ClientId, query["client_id"]);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/connections/callback/google", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.upload", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent select_account", query["prompt"]);
        Assert.Equal("false", query["include_granted_scopes"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var response = await admin.Browser.GetAsync($"/api/brands/{brandId}/connections/tiktok/connect", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Giving_consent_keeps_the_connection_while_the_admin_chooses_an_account()
    {
        var admin = await Workspaces.SignInAsync(_api, "Neha Rao");
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);

        var callback = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        Assert.Equal(HttpStatusCode.Redirect, callback.Response.StatusCode);
        Assert.Equal($"/{handle}/settings/connections?choose=google-ads", callback.Location);
        var connection = await Connections.GetAsync(admin.Browser, brandId, "google-ads");
        Assert.Equal("choosingAccount", connection.GetProperty("state").GetString());
        Assert.Equal(admin.UserId, connection.GetProperty("connectedBy").GetGuid());
        Assert.Equal("Neha Rao", connection.GetProperty("connectedByName").GetString());
        Assert.Contains("connection.connected", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task The_refresh_token_is_never_stored_in_plain_text()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var callback = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        var stored = await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads");
        Assert.NotNull(stored);
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(callback.Issued.RefreshToken)));
    }

    [Fact]
    public async Task Consent_without_every_scope_is_refused_and_given_back()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);

        var callback = await Connections.CompleteAsync(
            _api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "youtube"),
            scopes: ["https://www.googleapis.com/auth/youtube.readonly"]);

        Assert.Equal($"/{handle}/settings/connections?error=scopes_missing", callback.Location);
        Assert.Contains(callback.Issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "youtube")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Cancelling_at_Google_returns_to_the_settings_page()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var state = QueryHelpers.ParseQuery((await Connections.StartAsync(admin.Browser, brandId, "ga4")).Query)["state"].ToString();

        var response = await admin.Browser.GetAsync($"/api/connections/callback/google?error=access_denied&state={Uri.EscapeDataString(state)}", Token);

        Assert.Equal($"/{handle}/settings/connections?error=cancelled", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_tampered_state_changes_nothing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        var query = QueryHelpers.ParseQuery(authorize.Query);
        var state = query["state"].ToString();
        var tampered = state[..^4] + (state.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await admin.Browser.GetAsync($"/api/connections/callback/google?code=x&state={Uri.EscapeDataString(tampered)}", Token);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Consent_that_comes_back_after_ten_minutes_has_expired()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        _api.Clock.Advance(TimeSpan.FromMinutes(11));

        var callback = await Connections.CompleteAsync(_api, admin.Browser, authorize);

        Assert.Equal($"/{handle}/settings/connections?error=expired", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_replayed_callback_is_refused()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var first = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        var replay = await admin.Browser.GetAsync(first.Url, Token);

        Assert.Equal($"/{handle}/settings/connections?error=failed", replay.Headers.Location!.OriginalString);
        Assert.Single(await Workspaces.AuditActionsAsync(admin.Browser, brandId), action => action == "connection.connected");
    }

    [Fact]
    public async Task A_callback_in_someone_elses_session_changes_nothing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        var stranger = await Workspaces.SignInAsync(_api, "Stranger");

        var callback = await Connections.CompleteAsync(_api, stranger.Browser, authorize);

        Assert.Equal("/", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
        Assert.DoesNotContain("authorization_code", _api.GoogleApis.Requests);
    }

    [Fact]
    public async Task Someone_who_stopped_being_an_admin_cannot_finish_connecting()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        var authorize = await Connections.StartAsync(other.Browser, brandId, "google-ads");
        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        var callback = await Connections.CompleteAsync(_api, other.Browser, authorize);

        Assert.Equal("/", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(owner.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_connection_left_unfinished_for_an_hour_shows_as_not_connected()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        _api.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Reconnecting_revokes_the_old_token_only_when_its_Google_account_is_done_with_Kairo()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var first = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-a");

        var second = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-b");
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-b");

        Assert.Contains(first.Issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.DoesNotContain(second.Issued.RefreshToken, _api.GoogleApis.Revoked);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
