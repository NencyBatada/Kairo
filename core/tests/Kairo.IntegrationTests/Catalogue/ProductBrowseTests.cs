using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductBrowseTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_active_products_by_title_a_page_at_a_time()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("SKU-1", title: "Echo"),
            Catalogues.Row("SKU-2", title: "Alpha"),
            Catalogues.Row("SKU-3", title: "Delta"),
            Catalogues.Row("SKU-4", title: "Bravo"),
            Catalogues.Row("SKU-5", title: "Charlie")));

        var page = await ListAsync(admin.Browser, brandId, "?page=2&pageSize=2");

        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, page.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Charlie", "Delta" }, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToArray());
    }

    [Fact]
    public async Task A_product_shows_its_details_with_plain_prices()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", price: "\"1,499.50 INR\"")));

        var item = Assert.Single((await ListAsync(admin.Browser, brandId)).GetProperty("items").EnumerateArray());

        Assert.Equal("SKU-1", item.GetProperty("offerId").GetString());
        Assert.Equal("Runner SKU-1", item.GetProperty("title").GetString());
        Assert.Equal("https://acme.example/p/SKU-1", item.GetProperty("link").GetString());
        Assert.Equal("https://acme.example/i/SKU-1.jpg", item.GetProperty("imageLink").GetString());
        Assert.Equal("1499.5", item.GetProperty("price").GetRawText());
        Assert.Equal("INR", item.GetProperty("currency").GetString());
        Assert.Equal("in_stock", item.GetProperty("availability").GetString());
        Assert.Equal("Shoes", item.GetProperty("category").GetString());
        Assert.Empty(item.GetProperty("fixed").EnumerateArray());
        Assert.Empty(item.GetProperty("missing").EnumerateArray());
        Assert.Equal("active", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Filters_by_search_category_availability_and_missing_details()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("SKU-1", title: "Trail Runner"),
            Catalogues.Row("SKU-2", "out_of_stock", title: "Road Runner"),
            Catalogues.Row("SKU-3", title: "Wool Socks", category: "Socks"),
            "SKU-4,,https://acme.example/p/SKU-4,,,,Socks\n"));

        Assert.Equal(new[] { "SKU-2", "SKU-1" }, await OfferIdsAsync(admin.Browser, brandId, "?search=runner"));
        Assert.Equal(new[] { "SKU-3" }, await OfferIdsAsync(admin.Browser, brandId, "?search=SKU-3"));
        Assert.Equal(new[] { "SKU-4", "SKU-3" }, await OfferIdsAsync(admin.Browser, brandId, "?category=Socks"));
        Assert.Equal(new[] { "SKU-2" }, await OfferIdsAsync(admin.Browser, brandId, "?availability=out_of_stock"));
        Assert.Equal(new[] { "SKU-4" }, await OfferIdsAsync(admin.Browser, brandId, "?missing=any"));
        Assert.Equal(new[] { "SKU-4" }, await OfferIdsAsync(admin.Browser, brandId, "?missing=stock"));
        Assert.Empty(await OfferIdsAsync(admin.Browser, brandId, "?missing=link"));
        var incomplete = Assert.Single((await ListAsync(admin.Browser, brandId, "?missing=image")).GetProperty("items").EnumerateArray());
        Assert.Equal(
            new[] { "title", "price", "image", "stock" },
            incomplete.GetProperty("missing").EnumerateArray().Select(name => name.GetString()).ToArray());
    }

    [Fact]
    public async Task Lists_removed_products_on_request()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        Assert.Equal(new[] { "SKU-2" }, await OfferIdsAsync(admin.Browser, brandId, "?status=removed"));
        Assert.Equal(new[] { "SKU-1" }, await OfferIdsAsync(admin.Browser, brandId));
    }

    [Theory]
    [InlineData("?page=0", "page", "Use a page number from 1.")]
    [InlineData("?pageSize=201", "pageSize", "Use a page size from 1 to 200.")]
    [InlineData("?status=gone", "status", "Use active or removed.")]
    [InlineData("?availability=maybe", "availability", "Use in_stock, out_of_stock, preorder or backorder.")]
    [InlineData("?missing=colour", "missing", "Use any, title, price, link, image or stock.")]
    public async Task Refuses_a_bad_filter(string query, string field, string message)
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Products(brandId) + query, Token);

        Assert.Equal(new[] { message }, await Catalogues.ErrorsAsync(response, field));
    }

    [Fact]
    public async Task Shows_one_product_with_its_fixes_and_stock_events()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock")));
        var productId = Assert.Single((await ListAsync(admin.Browser, brandId)).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        var detail = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Products(brandId)}/{productId}", Token);

        Assert.Equal("SKU-1", detail.GetProperty("offerId").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("fixes").GetProperty("title").ValueKind);
        var stock = Assert.Single(detail.GetProperty("stockEvents").EnumerateArray());
        Assert.Equal("in_stock", stock.GetProperty("from").GetString());
        Assert.Equal("out_of_stock", stock.GetProperty("to").GetString());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync($"{Products(brandId)}/{Guid.NewGuid()}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Summarises_the_catalogue()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var products = new[]
        {
            Catalogues.Row("SKU-1"),
            Catalogues.Row("SKU-2", "out_of_stock"),
            Catalogues.Row("SKU-3", "preorder", category: "Socks"),
            "SKU-4,,https://acme.example/p/SKU-4,,,,Socks\n",
        };
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv([.. products, Catalogues.Row("SKU-5")]));
        var last = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(products));

        var summary = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/summary", Token);

        Assert.Equal(5, summary.GetProperty("products").GetInt32());
        Assert.Equal(4, summary.GetProperty("active").GetInt32());
        Assert.Equal(1, summary.GetProperty("inStock").GetInt32());
        Assert.Equal(1, summary.GetProperty("outOfStock").GetInt32());
        var missing = summary.GetProperty("missing");
        Assert.Equal(
            new[] { 1, 1, 0, 1, 1, 1 },
            new[] { "title", "price", "link", "image", "stock", "any" }.Select(name => missing.GetProperty(name).GetInt32()).ToArray());
        Assert.Equal(
            new[] { "Shoes 2", "Socks 2" },
            summary.GetProperty("categories").EnumerateArray().Select(c => $"{c.GetProperty("name").GetString()} {c.GetProperty("products").GetInt32()}").ToArray());
        Assert.Equal(last.GetProperty("id").GetGuid(), summary.GetProperty("lastImport").GetProperty("id").GetGuid());
        Assert.Equal("file", summary.GetProperty("source").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_empty_catalogue_has_an_empty_summary()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var summary = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/summary", Token);

        Assert.Equal(0, summary.GetProperty("products").GetInt32());
        Assert.Empty(summary.GetProperty("categories").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("lastImport").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("source").ValueKind);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_products()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Products(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Products(Guid brandId) => $"{Catalogues.Base(brandId)}/products";

    private static async Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        await browser.GetFromJsonAsync<JsonElement>(Products(brandId) + query, Token);

    private static async Task<string[]> OfferIdsAsync(HttpClient browser, Guid brandId, string query = "") =>
        (await ListAsync(browser, brandId, query)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("offerId").GetString()!).ToArray();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
