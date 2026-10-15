using System.Diagnostics;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.IntegrationTests.Catalogue;

[CollectionDefinition(nameof(LargeImportTests), DisableParallelization = true)]
public sealed class LargeImportCollection;

[Collection(nameof(LargeImportTests))]
public sealed class LargeImportTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    [Fact]
    public async Task A_catalogue_of_50000_products_imports_within_a_minute()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var text = Catalogues.Many(CatalogueFile.MaxProducts);

        var clock = Stopwatch.StartNew();
        var import = await Catalogues.ImportAsync(admin.Browser, brandId, text);
        clock.Stop();

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(CatalogueFile.MaxProducts, import.GetProperty("added").GetInt32());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"The import took {clock.Elapsed}.");
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
