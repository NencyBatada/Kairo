using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionLifecycleTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checking_a_healthy_connection_refreshes_access_and_records_the_check()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        var refreshes = _api.GoogleApis.Refreshes;
        _api.Clock.Advance(TimeSpan.FromMinutes(5));

        var check = await CheckAsync(admin, brandId, "google-ads");

        Assert.Equal(JsonValueKind.Null, check.GetProperty("problem").ValueKind);
        Assert.Equal("connected", check.GetProperty("connection").GetProperty("state").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), check.GetProperty("connection").GetProperty("checkedAt").GetDateTimeOffset());
        Assert.Equal(refreshes + 1, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_revoked_Google_grant_moves_the_connection_to_needs_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);

        var check = await CheckAsync(admin, brandId, "google-ads");

        var connection = check.GetProperty("connection");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("token_rejected", connection.GetProperty("reconnectReason").GetString());
        Assert.Contains("Connect it again", check.GetProperty("problem").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
        Assert.Contains("connection.needs_reconnect", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task A_failing_check_reports_the_problem_without_disconnecting()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "youtube");
        _api.GoogleApis.YouTubeChannels = [];

        var check = await CheckAsync(admin, brandId, "youtube");

        Assert.Contains("no longer manages", check.GetProperty("problem").GetString());
        Assert.Equal("connected", check.GetProperty("connection").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Disconnecting_erases_the_token_and_revokes_it_at_Google()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "merchant-center");

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/merchant-center", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "merchant-center")).GetProperty("state").GetString());
        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Contains("connection.disconnected", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task Disconnecting_revokes_the_token_only_after_the_removal_is_committed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "youtube");
        int? rowsSeenAtRevocation = null;
        _api.GoogleApis.BeforeRevoke = () => rowsSeenAtRevocation = CommittedConnections(brandId, "YouTube");

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/youtube", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal(0, rowsSeenAtRevocation);
    }

    [Fact]
    public async Task Disconnecting_still_works_when_Google_will_not_revoke()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "ga4");
        _api.GoogleApis.RevocationFails = true;

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/ga4", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "ga4")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Disconnecting_one_product_keeps_another_on_the_same_Google_account()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var ads = await Connections.ConnectAsync(_api, admin, brandId, "google-ads", subject: "shared-account");
        var youtube = await Connections.ConnectAsync(_api, admin, brandId, "youtube", subject: "shared-account");

        await admin.DeleteAsync($"/api/brands/{brandId}/connections/youtube", Token);

        Assert.DoesNotContain(youtube.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal(JsonValueKind.Null, (await CheckAsync(admin, brandId, "google-ads")).GetProperty("problem").ValueKind);

        await admin.DeleteAsync($"/api/brands/{brandId}/connections/google-ads", Token);

        Assert.Contains(ads.RefreshToken, _api.GoogleApis.Revoked);
    }

    [Fact]
    public async Task A_token_copied_to_another_workspace_cannot_be_used()
    {
        var (admin, first) = await WorkspaceAsync();
        var second = await Workspaces.CreateAsync(admin);
        await Connections.ConnectAsync(_api, admin, first, "google-ads");
        await Connections.ConnectAsync(_api, admin, second, "google-ads");

        await Connections.CopyStoredTokenAsync(sqlServer.KairoConnectionString, first, second, "google-ads");
        var check = await CheckAsync(admin, second, "google-ads");

        Assert.Equal("needsReconnect", check.GetProperty("connection").GetProperty("state").GetString());
        Assert.Equal("token_unreadable", check.GetProperty("connection").GetProperty("reconnectReason").GetString());
    }

    [Fact]
    public async Task Checking_needs_a_finished_connection()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Disconnecting_works_when_the_token_cannot_be_decrypted()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        await Connections.SetKeyVersionAsync(sqlServer.KairoConnectionString, brandId, "google-ads", "projects/elsewhere/cryptoKeyVersions/1");

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/google-ads", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "google-ads")).GetProperty("state").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private int CommittedConnections(Guid brandId, string product)
    {
        using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SET LOCK_TIMEOUT 2000;
            SELECT COUNT(*) FROM brands.Connections WITH (READCOMMITTEDLOCK) WHERE BrandId = @brand AND Product = @product;
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@product", product);
        try
        {
            return (int)command.ExecuteScalar()!;
        }
        catch (SqlException exception) when (exception.Number == 1222)
        {
            return -1;
        }
    }

    private async Task<(HttpClient Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin.Browser, await Workspaces.CreateAsync(admin.Browser));
    }

    private static async Task<JsonElement> CheckAsync(HttpClient admin, Guid brandId, string product)
    {
        var response = await admin.PostAsync($"/api/brands/{brandId}/connections/{product}/check", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
