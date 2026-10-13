using Kairo.Contracts.Brands;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Feeds;

public sealed class BrandListTests(SqlServer sqlServer) : IAsyncLifetime
{
    private KairoApiFactory _api = null!;

    public async ValueTask InitializeAsync() => _api = new KairoApiFactory(await IsolatedDatabase.CreateAsync(sqlServer));

    [Fact]
    public async Task System_work_sees_every_workspace()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var ids = new[]
        {
            await Workspaces.CreateAsync(first.Browser),
            await Workspaces.CreateAsync(first.Browser),
            await Workspaces.CreateAsync(second.Browser),
        };

        await using var scope = _api.Services.CreateAsyncScope();
        var brands = await scope.ServiceProvider.GetRequiredService<IBrandDirectory>().ListAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ids.Order(), brands.Select(brand => brand.Id).Order());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
