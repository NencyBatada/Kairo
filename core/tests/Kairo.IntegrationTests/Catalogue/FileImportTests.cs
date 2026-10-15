using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Catalogue.Format;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class FileImportTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task A_file_import_adds_the_products_and_is_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await Catalogues.PostFileAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var import = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var importId = import.GetProperty("id").GetGuid();
        Assert.Equal($"/api/brands/{brandId}/catalogue/imports/{importId}", response.Headers.Location!.OriginalString);
        Assert.Equal("file", import.GetProperty("kind").GetString());
        Assert.Equal("products.csv", import.GetProperty("fileName").GetString());
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(3, import.GetProperty("rows").GetInt32());
        Assert.Equal(3, import.GetProperty("valid").GetInt32());
        Assert.Equal(3, import.GetProperty("added").GetInt32());
        Assert.Equal(admin.UserId, import.GetProperty("startedBy").GetGuid());
        var products = await CatalogueRows.ProductsAsync(ConnectionString, brandId);
        Assert.Equal(new[] { "SKU-1", "SKU-2", "SKU-3" }, products.Select(p => p.OfferId).ToArray());
        Assert.All(products, product =>
        {
            Assert.Equal("active", product.Status);
            Assert.Equal(0, product.Missing);
            Assert.Equal(importId, product.LastImportId);
        });
        Assert.Contains("catalogue.file_imported", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Members_can_list_imports_and_read_one_with_its_problems()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var import = await Catalogues.ImportAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2", price: "cheap"), ",no id,,,,,\n"));
        var approver = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "approver");

        var listed = await approver.Browser.GetFromJsonAsync<JsonElement>(Catalogues.Imports(brandId), Token);
        var detail = await approver.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Imports(brandId)}/{import.GetProperty("id").GetGuid()}", Token);

        Assert.False(Assert.Single(listed.EnumerateArray()).TryGetProperty("problems", out _));
        Assert.Equal(1, detail.GetProperty("invalid").GetInt32());
        Assert.Equal(
            new[]
            {
                "3 price The price isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty.",
                "4 id The id is missing.",
            },
            detail.GetProperty("problems").EnumerateArray()
                .Select(p => $"{p.GetProperty("row").GetInt32()} {p.GetProperty("attribute").GetString()} {p.GetProperty("message").GetString()}")
                .ToArray());
    }

    [Fact]
    public async Task A_second_import_updates_changed_products_and_counts_the_rest()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        var import = await Catalogues.ImportAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1", title: "Runner Pro"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3"), Catalogues.Row("SKU-4")));

        Assert.Equal((1, 1, 2, 0), Counts(import));
        var products = await CatalogueRows.ProductsAsync(ConnectionString, brandId);
        Assert.Equal("Runner Pro", products.Single(p => p.OfferId == "SKU-1").Title);
        Assert.Equal(import.GetProperty("id").GetGuid(), products.Single(p => p.OfferId == "SKU-1").LastImportId);
        Assert.NotEqual(import.GetProperty("id").GetGuid(), products.Single(p => p.OfferId == "SKU-2").LastImportId);
    }

    [Fact]
    public async Task Products_left_out_are_removed_and_come_back_later()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        var removing = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));
        var removed = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-3");
        var returning = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));
        var back = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-3");

        Assert.Equal((0, 0, 2, 1), Counts(removing));
        Assert.Equal("removed", removed.Status);
        Assert.NotNull(removed.RemovedAt);
        Assert.Equal((0, 1, 2, 0), Counts(returning));
        Assert.Equal("active", back.Status);
        Assert.Null(back.RemovedAt);
    }

    [Fact]
    public async Task An_id_whose_case_changes_is_the_same_product()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("sku-1", title: "Runner SKU-1")));

        Assert.Equal((0, 1, 0, 0), Counts(import));
        Assert.Equal("sku-1", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).OfferId);
    }

    [Fact]
    public async Task Stock_changes_are_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var soldOut = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock"), Catalogues.Row("SKU-2")));
        var dropped = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-2")));
        var back = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "preorder"), Catalogues.Row("SKU-2")));

        var productId = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-1").Id;
        var events = await CatalogueRows.StockEventsAsync(ConnectionString, brandId);
        Assert.Equal(new[] { "in_stock>out_of_stock", "out_of_stock>removed", "removed>preorder" }, events.Select(e => $"{e.From}>{e.To}").ToArray());
        Assert.All(events, e => Assert.Equal(productId, e.ProductId));
        Assert.Equal(
            new[] { soldOut, dropped, back }.Select(import => import.GetProperty("id").GetGuid()).ToArray(),
            events.Select(e => e.ImportId).ToArray());
    }

    [Fact]
    public async Task Stock_that_becomes_unknown_records_no_stock_change()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "")));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock")));

        var events = await CatalogueRows.StockEventsAsync(ConnectionString, brandId);
        Assert.Equal(new[] { ">out_of_stock" }, events.Select(e => $"{e.From}>{e.To}").ToArray());
    }

    [Fact]
    public async Task A_large_import_in_another_workspace_does_not_hold_this_one_up()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        await using var other = await CatalogueLocks.WriteWithoutCommittingAsync(ConnectionString, Guid.NewGuid(), 6000);
        var clock = Stopwatch.StartNew();

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock"), Catalogues.Row("SKU-2")));

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"The import took {clock.Elapsed}.");
    }

    [Fact]
    public async Task The_same_file_again_is_unchanged_and_stored_once()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Csv(Catalogues.Row("SKU-1"));
        var first = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        var second = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        Assert.Equal("unchanged", second.GetProperty("status").GetString());
        Assert.Equal((0, 0, 0, 0), Counts(second));
        Assert.Equal(first.GetProperty("id").GetGuid(), Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).LastImportId);
        var imports = await CatalogueRows.ImportsAsync(ConnectionString, brandId);
        Assert.NotNull(imports[0].StorageKey);
        Assert.Null(imports[1].StorageKey);
    }

    [Fact]
    public async Task The_raw_file_is_kept_in_the_import_bucket()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Csv(Catalogues.Row("SKU-1"));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        var key = Assert.Single(await CatalogueRows.ImportsAsync(ConnectionString, brandId)).StorageKey;
        Assert.Equal($"{brandId:N}/imports/{import.GetProperty("id").GetGuid():N}", key);
        await using var stream = await _api.Services.GetRequiredService<IFileStore>().OpenAsync(FileBuckets.CatalogueImports, key!, Token);
        using var reader = new StreamReader(stream);
        Assert.Equal(text, await reader.ReadToEndAsync(Token));
    }

    [Fact]
    public async Task A_file_that_cant_be_read_fails_and_changes_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, "title,price\nRunner,1499\n");

        Assert.Equal("failed", import.GetProperty("status").GetString());
        Assert.Equal("no_id_column", import.GetProperty("errorCode").GetString());
        Assert.Equal("The file has no id column. Every product needs an id.", import.GetProperty("errorMessage").GetString());
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
    }

    [Fact]
    public async Task A_file_with_no_valid_rows_is_suspicious_and_changes_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Header);

        Assert.Equal("suspicious", import.GetProperty("status").GetString());
        Assert.Equal("no_valid_rows", import.GetProperty("errorCode").GetString());
        Assert.Equal("No product in this import could be read, so Kairo changed nothing.", import.GetProperty("errorMessage").GetString());
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
    }

    [Fact]
    public async Task Removing_more_than_half_of_20_or_more_products_is_suspicious_unless_allowed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(20));

        var refused = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(9));
        var activeAfterRefusal = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Count(p => p.Status == "active");
        var allowed = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(9), allowLargeRemoval: true);

        Assert.Equal("suspicious", refused.GetProperty("status").GetString());
        Assert.Equal("large_removal", refused.GetProperty("errorCode").GetString());
        Assert.Equal(
            "This import would remove 11 of your 20 products, so Kairo changed nothing. If that's right, import the file again and allow large removals.",
            refused.GetProperty("errorMessage").GetString());
        Assert.Equal(20, activeAfterRefusal);
        Assert.Equal("succeeded", allowed.GetProperty("status").GetString());
        Assert.Equal(11, allowed.GetProperty("removed").GetInt32());
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(19, 1)]
    public async Task Smaller_removals_go_ahead(int before, int after)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(before));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(after));

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(before - after, import.GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task Each_import_publishes_a_live_event()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var published = Assert.Single(await OutboxRows.ReadAsync(ConnectionString, brandId), row => row.Type == "catalogue.imported");
        var payload = JsonDocument.Parse(published.Payload).RootElement;
        Assert.Equal(import.GetProperty("id").GetGuid(), payload.GetProperty("importId").GetGuid());
        Assert.Equal("succeeded", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_admins_can_import()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");

        var response = await Catalogues.PostFileAsync(marketer.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Choosing_no_file_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        using var form = new MultipartFormDataContent { { new StringContent("true"), "allowLargeRemoval" } };

        var response = await admin.Browser.PostAsync(Catalogues.Imports(brandId), form, Token);

        Assert.Equal(new[] { "Choose a product file to upload." }, await Catalogues.ErrorsAsync(response, "file"));
    }

    [Fact]
    public async Task A_file_over_50_MB_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = Catalogues.Csv(Catalogues.Row("SKU-1")) + new string(' ', CatalogueFile.MaxBytes);

        var response = await Catalogues.PostFileAsync(admin.Browser, brandId, huge);

        Assert.Equal(new[] { "The file is larger than 50 MB." }, await Catalogues.ErrorsAsync(response, "file"));
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task An_import_while_another_is_running_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        HttpResponseMessage response;
        await using (await CatalogueLocks.HoldAsync(ConnectionString, brandId))
        {
            response = await Catalogues.PostFileAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Kairo is already importing this catalogue. Try again in a minute.", await Catalogues.ProblemTitleAsync(response));
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_imports()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Catalogues.Imports(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_catalogue_of_50000_products_imports_within_a_minute()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Many(CatalogueFile.MaxProducts);

        var clock = Stopwatch.StartNew();
        var import = await Catalogues.ImportAsync(admin.Browser, brandId, text);
        clock.Stop();

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(CatalogueFile.MaxProducts, import.GetProperty("added").GetInt32());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"The import took {clock.Elapsed}.");
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static (int Added, int Updated, int Unchanged, int Removed) Counts(JsonElement import) => (
        import.GetProperty("added").GetInt32(),
        import.GetProperty("updated").GetInt32(),
        import.GetProperty("unchanged").GetInt32(),
        import.GetProperty("removed").GetInt32());

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
