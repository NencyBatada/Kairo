using Kairo.Contracts.Connections;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionAccessTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Other_modules_get_the_account_and_a_working_access_token()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");

        var first = await InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token));
        var refreshes = _api.GoogleApis.Refreshes;
        var second = await InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token));

        Assert.Equal("1111111111", first.ExternalId);
        Assert.StartsWith("access-", first.AccessToken);
        Assert.Equal(first.AccessToken, second.AccessToken);
        Assert.Equal(refreshes, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_product_that_is_not_connected_is_unavailable()
    {
        var (_, brandId) = await WorkspaceAsync();

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.BigQuery, Token)));

        Assert.Equal("not_connected", unavailable.Reason);
    }

    [Fact]
    public async Task A_rejected_token_makes_the_connection_unavailable_and_asks_for_a_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        _api.Clock.Advance(TimeSpan.FromHours(2));

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token)));
        var status = await InBrandAsync(brandId, connections => connections.GetStatusAsync(ConnectedProduct.GoogleAds, Token));

        Assert.Equal("token_rejected", unavailable.Reason);
        Assert.Equal(ConnectionState.NeedsReconnect, status.State);
    }

    [Fact]
    public async Task A_rejected_token_stays_rejected_when_the_callers_transaction_rolls_back()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        _api.Clock.Advance(TimeSpan.FromHours(2));

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
                scope.ServiceProvider.GetRequiredService<IConnections>().GetAccessAsync(ConnectedProduct.GoogleAds, Token));
            await database.RollbackAsync(Token);
        }

        var status = await InBrandAsync(brandId, connections => connections.GetStatusAsync(ConnectedProduct.GoogleAds, Token));
        Assert.Equal(ConnectionState.NeedsReconnect, status.State);
        Assert.Contains("connection.needs_reconnect", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task A_token_sealed_with_another_key_is_kept_rather_than_erased()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        await Connections.SetKeyVersionAsync(
            sqlServer.KairoConnectionString, brandId, "google-ads",
            "projects/kairo2/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens/cryptoKeyVersions/1");
        _api.Clock.Advance(TimeSpan.FromHours(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token)));

        Assert.NotNull(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
        var status = await InBrandAsync(brandId, connections => connections.GetStatusAsync(ConnectedProduct.GoogleAds, Token));
        Assert.Equal(ConnectionState.Connected, status.State);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(HttpClient Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin.Browser, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<T> InBrandAsync<T>(Guid brandId, Func<IConnections, Task<T>> action)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await action(scope.ServiceProvider.GetRequiredService<IConnections>());
    }
}
