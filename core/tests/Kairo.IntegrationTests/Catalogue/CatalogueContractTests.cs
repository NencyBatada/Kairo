using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Catalogue;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class CatalogueContractTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Categories_count_only_active_products_in_stock()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("A-1", title: "Rain Boot", category: "Shoes > Rain"),
            Catalogues.Row("A-2", title: "Puddle Runner", category: "Shoes > Rain"),
            Catalogues.Row("A-3", title: "Drizzle Sneaker", category: "Shoes > Rain"),
            Catalogues.Row("A-4", title: "Ankle Splash", category: "Shoes > Rain"),
            Catalogues.Row("B-1", "out_of_stock", "Winter Boot", category: "Shoes > Winter"),
            Catalogues.Row("C-1", "preorder", "Future Shoe", category: "Shoes > Future"),
            Catalogues.Row("D-1", title: "School Shoe", category: "Shoes > School")));

        var categories = await WithCatalogueAsync(brandId, catalogue => catalogue.ListInStockCategoriesAsync(Token));

        Assert.Equal(["Shoes > Rain", "Shoes > School"], categories.Select(category => category.Name));
        Assert.Equal(4, categories[0].InStock);
        Assert.Equal(["Ankle Splash", "Drizzle Sneaker", "Puddle Runner"], categories[0].SampleTitles);
    }

    [Fact]
    public async Task A_fixed_category_counts_like_one_from_the_feed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Header + "A-1,Rain Boot,https://acme.example/p/A-1,https://acme.example/i/A-1.jpg,1499.00 INR,in_stock,\n");
        var product = (await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/products", Token)).GetProperty("items")[0];
        (await admin.Browser.PatchAsJsonAsync($"{Catalogues.Base(brandId)}/products/{product.GetProperty("id").GetGuid()}/fixes", new { category = "Shoes > Rain" }, Token))
            .EnsureSuccessStatusCode();

        var categories = await WithCatalogueAsync(brandId, catalogue => catalogue.ListInStockCategoriesAsync(Token));

        Assert.Equal("Shoes > Rain", Assert.Single(categories).Name);
    }

    [Fact]
    public async Task Products_in_matching_categories_are_ranked_by_the_search_words_in_their_titles()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("A-1", title: "Classic Runner", category: "Shoes > Rain"),
            Catalogues.Row("A-2", title: "Waterproof Rain Boots", category: "Shoes > Rain"),
            Catalogues.Row("A-3", "out_of_stock", "Rain Boots Pro", category: "Shoes > Rain"),
            Catalogues.Row("B-1", title: "Rain Hat", category: "Hats")));

        var products = await WithCatalogueAsync(brandId, catalogue => catalogue.FindInStockAsync(["Shoes > Rain"], ["rain boots"], 5, Token));

        Assert.Equal(["Waterproof Rain Boots", "Classic Runner"], products.Select(product => product.Title));
        Assert.Equal(1499m, products[0].Price);
        Assert.Equal("INR", products[0].Currency);
        Assert.Equal("https://acme.example/i/A-2.jpg", products[0].ImageLink);
    }

    [Fact]
    public async Task In_stock_products_carry_the_details_ads_are_written_from()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(
            admin.Browser,
            brandId,
            "id,title,description,brand,gtin,link,image_link,additional_image_link,price,availability,product_type\n"
            + "A-1,Rain Boot,Waterproof rubber boot,Acme,12345678,https://acme.example/p/A-1,https://acme.example/i/A-1.jpg,https://acme.example/i/A-1-side.jpg,1499.00 INR,in_stock,Shoes\n");
        var id = (await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/products", Token)).GetProperty("items")[0].GetProperty("id").GetGuid();

        var product = Assert.Single(await WithCatalogueAsync(brandId, catalogue => catalogue.GetInStockAsync([id], Token)));

        Assert.Equal(("Waterproof rubber boot", "Acme", "12345678", "in_stock"), (product.Description, product.Brand, product.Gtin, product.Availability));
        Assert.Equal(["https://acme.example/i/A-1-side.jpg"], product.AdditionalImageLinks!);
    }

    [Fact]
    public async Task Without_a_matching_category_no_product_matches()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1", title: "Gumboots for kids", category: "Kids")));

        var noCategory = await WithCatalogueAsync(brandId, catalogue => catalogue.FindInStockAsync([], ["kids gumboots"], 5, Token));
        var otherCategory = await WithCatalogueAsync(brandId, catalogue => catalogue.FindInStockAsync(["Adults"], ["kids gumboots"], 5, Token));

        Assert.Empty(noCategory);
        Assert.Empty(otherCategory);
    }

    [Fact]
    public async Task Only_products_still_in_stock_come_back_by_id()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1"), Catalogues.Row("A-2", "out_of_stock")));
        var ids = (await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/products", Token))
            .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

        var products = await WithCatalogueAsync(brandId, catalogue => catalogue.GetInStockAsync(ids, Token));
        var (_, otherBrand) = await WorkspaceAsync();
        var fromOtherBrand = await WithCatalogueAsync(otherBrand, catalogue => catalogue.GetInStockAsync(ids, Token));

        Assert.Equal("A-1", Assert.Single(products).OfferId);
        Assert.Empty(fromOtherBrand);
    }

    [Fact]
    public async Task Category_stock_adds_up_the_units_of_products_in_stock()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, """
            id,title,link,image_link,price,availability,product_type,g:quantity
            A-1,Rain Boot,https://acme.example/p/1,https://acme.example/i/1.jpg,1499.00 INR,in_stock,Shoes > Rain,"1,200"
            A-2,Puddle Runner,https://acme.example/p/2,https://acme.example/i/2.jpg,1499.00 INR,in_stock,Shoes > Rain,30
            A-3,Drizzle Sneaker,https://acme.example/p/3,https://acme.example/i/3.jpg,1499.00 INR,in_stock,Shoes > Rain,lots
            A-4,Ankle Splash,https://acme.example/p/4,https://acme.example/i/4.jpg,1499.00 INR,out_of_stock,Shoes > Rain,900

            """);

        var stock = await WithCatalogueAsync(brandId, catalogue => catalogue.GetCategoryStockAsync("Shoes > Rain", Token));

        Assert.Equal(new CategoryStock(3, 2, 1230), stock);
        Assert.Equal(new CategoryStock(0, 0, 0), await WithCatalogueAsync(brandId, catalogue => catalogue.GetCategoryStockAsync("Hats", Token)));
    }

    [Fact]
    public async Task The_latest_update_is_the_last_import_that_changed_the_catalogue()
    {
        var (admin, brandId) = await WorkspaceAsync();
        Assert.Null(await WithCatalogueAsync(brandId, catalogue => catalogue.LatestImportAsync(Token)));
        var first = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1")));
        var second = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1"), Catalogues.Row("A-2")));

        var latest = await WithCatalogueAsync(brandId, catalogue => catalogue.LatestImportAsync(Token));

        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(second.GetProperty("id").GetGuid(), latest!.ImportId);
    }

    [Fact]
    public async Task Stock_reports_each_product_state_since_its_last_change_and_the_last_successful_update()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var empty = await WithCatalogueAsync(brandId, catalogue => catalogue.GetStockAsync([], Token));
        Assert.Null(empty.UpdatedAt);
        Assert.Empty(empty.Products);
        var firstSeen = _api.Clock.GetUtcNow();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1"), Catalogues.Row("A-2")));
        var a2 = (await WithCatalogueAsync(brandId, catalogue => catalogue.FindInStockAsync(["Shoes"], [], 10, Token))).Single(product => product.OfferId == "A-2");
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1", "out_of_stock"), Catalogues.Row("A-2")));
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        var unchanged = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1", "out_of_stock"), Catalogues.Row("A-2")));

        var stock = await WithCatalogueAsync(brandId, catalogue => catalogue.GetStockAsync([a2.Id, Guid.NewGuid()], Token));

        Assert.Equal("unchanged", unchanged.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), stock.UpdatedAt);
        var product = Assert.Single(stock.Products);
        Assert.Equal(("in_stock", firstSeen), (product.State, product.Since));
    }

    [Fact]
    public async Task Stock_marks_sold_out_and_removed_products_from_their_change()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1"), Catalogues.Row("A-2"), Catalogues.Row("A-3"), Catalogues.Row("A-4")));
        var products = await WithCatalogueAsync(brandId, catalogue => catalogue.FindInStockAsync(["Shoes"], [], 10, Token));
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        var changedAt = _api.Clock.GetUtcNow();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("A-1", "out_of_stock"), Catalogues.Row("A-2"), Catalogues.Row("A-3")));

        var stock = await WithCatalogueAsync(brandId, catalogue => catalogue.GetStockAsync([.. products.Select(product => product.Id)], Token));

        var states = stock.Products.ToDictionary(product => products.Single(item => item.Id == product.Id).OfferId);
        Assert.Equal(("out_of_stock", changedAt), (states["A-1"].State, states["A-1"].Since));
        Assert.Equal(("removed", changedAt), (states["A-4"].State, states["A-4"].Since));
        Assert.Equal("in_stock", states["A-2"].State);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<T> WithCatalogueAsync<T>(Guid brandId, Func<ICatalogue, Task<T>> read)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await read(scope.ServiceProvider.GetRequiredService<ICatalogue>());
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
