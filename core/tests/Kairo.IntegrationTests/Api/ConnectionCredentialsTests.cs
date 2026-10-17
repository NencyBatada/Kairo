using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionCredentialsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string ClientId = "brand-client.apps.googleusercontent.com";
    private const string ClientSecret = "brand-client-secret";

    private static readonly Dictionary<string, string[]> Scopes = new()
    {
        ["google-ads"] = ["https://www.googleapis.com/auth/adwords"],
        ["youtube"] = ["https://www.googleapis.com/auth/youtube.readonly", "https://www.googleapis.com/auth/youtube.upload"],
        ["ga4"] = ["https://www.googleapis.com/auth/analytics.readonly"],
    };

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_credentials_start_account_choice_with_the_brands_own_client()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");

        var response = await ConnectAsync(admin, brandId, "ga4", refreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("choosingAccount", view.GetProperty("state").GetString());
        Assert.Equal("clientCredentials", view.GetProperty("method").GetString());
        Assert.Equal(ClientId, view.GetProperty("clientId").GetString());
        Assert.Equal(Scopes["ga4"], view.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Contains(ClientId, _api.GoogleApis.RefreshClients);

        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/ga4/account", new { externalId = "777" }, Token);

        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var connection = await Connections.GetAsync(admin, brandId, "ga4");
        Assert.Equal("connected", connection.GetProperty("state").GetString());
        Assert.Equal("clientCredentials", connection.GetProperty("method").GetString());
    }

    [Fact]
    public async Task The_refresh_token_and_client_secret_are_stored_encrypted()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");

        (await ConnectAsync(admin, brandId, "ga4", refreshToken)).EnsureSuccessStatusCode();

        var storedToken = await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "ga4");
        var storedSecret = await Connections.StoredClientSecretAsync(sqlServer.KairoConnectionString, brandId, "ga4");
        Assert.NotNull(storedToken);
        Assert.NotNull(storedSecret);
        Assert.DoesNotContain(refreshToken, Encoding.UTF8.GetString(storedToken), StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, Encoding.UTF8.GetString(storedSecret), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_access_refreshes_through_the_brands_own_client()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await ConnectAndChooseAsync(admin, brandId, "google-ads", IssueToken("google-ads"));
        var refreshes = _api.GoogleApis.RefreshClients.Count;

        var check = await admin.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token);

        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await check.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("problem").ValueKind);
        Assert.Equal(refreshes + 1, _api.GoogleApis.RefreshClients.Count);
        Assert.Equal(ClientId, _api.GoogleApis.RefreshClients.Last());
    }

    [Fact]
    public async Task A_wrong_client_secret_is_rejected_and_nothing_is_stored()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");

        var response = await ConnectAsync(admin, brandId, "ga4", refreshToken, clientSecret: "not-the-secret");

        await AssertRejectedAsync(response, "Google rejected these credentials");
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "ga4")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        var (admin, brandId) = await WorkspaceAsync();
        IssueToken("ga4");

        var response = await ConnectAsync(admin, brandId, "ga4", "made-up-token");

        await AssertRejectedAsync(response, "Google rejected these credentials");
    }

    [Fact]
    public async Task A_token_without_the_products_access_is_rejected_and_not_revoked()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("youtube", ["https://www.googleapis.com/auth/youtube.readonly"]);

        var response = await ConnectAsync(admin, brandId, "youtube", refreshToken);

        await AssertRejectedAsync(response, "https://www.googleapis.com/auth/youtube.upload");
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "youtube")).GetProperty("state").GetString());
        Assert.DoesNotContain(refreshToken, _api.GoogleApis.Revoked);
    }

    [Fact]
    public async Task Missing_fields_are_reported_without_calling_Google()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/brands/{brandId}/connections/ga4/credentials",
            new { clientId = " ", clientSecret = "", refreshToken = (string?)null },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("clientId", out _));
        Assert.True(errors.TryGetProperty("clientSecret", out _));
        Assert.True(errors.TryGetProperty("refreshToken", out _));
        Assert.Empty(_api.GoogleApis.RefreshClients);
    }

    [Fact]
    public async Task Only_admins_can_connect_with_credentials()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await ConnectAsync(marketer.Browser, brandId, "ga4", IssueToken("ga4"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disconnecting_erases_the_credentials_without_revoking_the_brands_token()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");
        await ConnectAndChooseAsync(admin, brandId, "ga4", refreshToken);

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/ga4", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "ga4")).GetProperty("state").GetString());
        Assert.Null(await Connections.StoredClientSecretAsync(sqlServer.KairoConnectionString, brandId, "ga4"));
        Assert.Empty(_api.GoogleApis.Revoked);
    }

    [Fact]
    public async Task Replacing_a_Google_sign_in_connection_with_credentials_revokes_Kairos_old_token()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "ga4");

        (await ConnectAsync(admin, brandId, "ga4", IssueToken("ga4"))).EnsureSuccessStatusCode();

        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal("clientCredentials", (await Connections.GetAsync(admin, brandId, "ga4")).GetProperty("method").GetString());
    }

    [Fact]
    public async Task Replacing_a_credentials_connection_with_Google_sign_in_keeps_the_brands_token()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");
        await ConnectAndChooseAsync(admin, brandId, "ga4", refreshToken);

        await Connections.ConnectAsync(_api, admin, brandId, "ga4");

        Assert.DoesNotContain(refreshToken, _api.GoogleApis.Revoked);
        var connection = await Connections.GetAsync(admin, brandId, "ga4");
        Assert.Equal("google", connection.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, connection.GetProperty("clientId").ValueKind);
        Assert.Null(await Connections.StoredClientSecretAsync(sqlServer.KairoConnectionString, brandId, "ga4"));
    }

    [Fact]
    public async Task An_admin_who_leaves_releases_credentials_without_revoking_them()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        var refreshToken = IssueToken("ga4");
        await ConnectAndChooseAsync(other.Browser, brandId, "ga4", refreshToken);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        var connection = await Connections.GetAsync(owner.Browser, brandId, "ga4");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("clientCredentials", connection.GetProperty("method").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "ga4"));
        Assert.Null(await Connections.StoredClientSecretAsync(sqlServer.KairoConnectionString, brandId, "ga4"));
        Assert.DoesNotContain(refreshToken, _api.GoogleApis.Revoked);
    }

    [Fact]
    public async Task A_deleted_brand_client_moves_the_connection_to_needs_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await ConnectAndChooseAsync(admin, brandId, "ga4", IssueToken("ga4"));
        _api.GoogleApis.ForgetClient(ClientId);

        var check = await admin.PostAsync($"/api/brands/{brandId}/connections/ga4/check", null, Token);

        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var connection = (await check.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("connection");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Null(await Connections.StoredClientSecretAsync(sqlServer.KairoConnectionString, brandId, "ga4"));
    }

    [Fact]
    public async Task The_audit_entry_names_the_method_but_never_the_secrets()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var refreshToken = IssueToken("ga4");

        (await ConnectAsync(admin, brandId, "ga4", refreshToken)).EnsureSuccessStatusCode();

        var entry = await Workspaces.LatestAuditEntryAsync(admin, brandId);
        Assert.Equal("connection.connected", entry.GetProperty("action").GetString());
        var text = entry.GetRawText();
        Assert.Contains("ClientCredentials", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, text, StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private string IssueToken(string product, string[]? scopes = null)
    {
        _api.GoogleApis.RegisterClient(ClientId, ClientSecret);
        return _api.GoogleApis.IssueClientToken(ClientId, scopes ?? Scopes[product]);
    }

    private static Task<HttpResponseMessage> ConnectAsync(
        HttpClient admin,
        Guid brandId,
        string product,
        string refreshToken,
        string clientSecret = ClientSecret) =>
        admin.PostAsJsonAsync(
            $"/api/brands/{brandId}/connections/{product}/credentials",
            new { clientId = ClientId, clientSecret, refreshToken },
            Token);

    private static async Task ConnectAndChooseAsync(HttpClient admin, Guid brandId, string product, string refreshToken)
    {
        (await ConnectAsync(admin, brandId, product, refreshToken)).EnsureSuccessStatusCode();
        var chosen = await admin.PostAsJsonAsync(
            $"/api/brands/{brandId}/connections/{product}/account",
            new { externalId = Connections.DefaultAccount(product) },
            Token);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string titlePart)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Contains(titlePart, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    private async Task<(HttpClient Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin.Browser, await Workspaces.CreateAsync(admin.Browser));
    }
}
