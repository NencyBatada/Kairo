using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductFixTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string Untitled = "SKU-1,,https://acme.example/p/SKU-1,,,in_stock,\n";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task A_fix_fills_missing_details()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(
            Fixes(brandId, productId),
            new { title = "Trail Runner", imageLink = "https://acme.example/i/1.jpg", category = "Shoes" },
            Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("Trail Runner", product.GetProperty("title").GetString());
        Assert.Equal("https://acme.example/i/1.jpg", product.GetProperty("imageLink").GetString());
        Assert.Equal("Shoes", product.GetProperty("category").GetString());
        Assert.Equal(new[] { "title", "imageLink", "category" }, Names(product, "fixed"));
        Assert.Equal(new[] { "price" }, Names(product, "missing"));
        Assert.Contains("catalogue.product_fixed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
        var published = Assert.Single(await OutboxRows.ReadAsync(ConnectionString, brandId), row => row.Type == "catalogue.product_changed");
        Assert.Equal(productId, JsonDocument.Parse(published.Payload).RootElement.GetProperty("productId").GetGuid());
    }

    [Fact]
    public async Task A_price_fix_uses_the_brands_currency_unless_given()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var inBrandCurrency = await FixAsync(admin.Browser, brandId, productId, new { price = 1299.5m });
        var inDollars = await FixAsync(admin.Browser, brandId, productId, new { price = 15, currency = "usd" });

        Assert.Equal(1299.5m, inBrandCurrency.GetProperty("price").GetDecimal());
        Assert.Equal("INR", inBrandCurrency.GetProperty("currency").GetString());
        Assert.Equal(15m, inDollars.GetProperty("price").GetDecimal());
        Assert.Equal("USD", inDollars.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_price_fix_with_a_blank_currency_uses_the_brands_currency()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var fixedProduct = await FixAsync(admin.Browser, brandId, productId, new { price = 10, currency = "" });

        Assert.Equal(10m, fixedProduct.GetProperty("price").GetDecimal());
        Assert.Equal("INR", fixedProduct.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_fix_lasts_while_the_import_leaves_the_field_empty()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Untitled.Replace("in_stock", "out_of_stock")));

        var product = await GetAsync(admin.Browser, brandId, productId);
        Assert.Equal("Trail Runner", product.GetProperty("title").GetString());
        Assert.Equal(new[] { "title" }, Names(product, "fixed"));
    }

    [Fact]
    public async Task A_fix_is_dropped_once_the_import_supplies_the_field()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Untitled.Replace("SKU-1,,", "SKU-1,Runner Pro,")));

        var product = await GetAsync(admin.Browser, brandId, productId);
        Assert.Equal("Runner Pro", product.GetProperty("title").GetString());
        Assert.Empty(Names(product, "fixed"));
        Assert.DoesNotContain("Trail Runner", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Fixes);
    }

    [Fact]
    public async Task Null_clears_a_fix()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        var product = await FixAsync(admin.Browser, brandId, productId, new { title = (string?)null });

        Assert.Equal(JsonValueKind.Null, product.GetProperty("title").ValueKind);
        Assert.Empty(Names(product, "fixed"));
        Assert.Contains("title", Names(product, "missing"));
    }

    [Fact]
    public async Task A_field_the_import_supplies_cant_be_fixed()
    {
        var (admin, brandId, productId) = await ProductAsync(Catalogues.Row("SKU-1"));

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Other", imageLink = "https://acme.example/i/2.jpg" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal(new[] { "The import already supplies the title, so it can't be fixed here." }, errors.GetProperty("title").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(new[] { "The import already supplies the image link, so it can't be fixed here." }, errors.GetProperty("imageLink").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Theory]
    [InlineData("{\"stock\":\"in_stock\"}", "stock", "Stock can't be fixed here. Change it in the file or feed.")]
    [InlineData("{\"availability\":\"in_stock\"}", "availability", "Stock can't be fixed here. Change it in the file or feed.")]
    [InlineData("{\"colour\":\"red\"}", "colour", "Kairo can't fix this field.")]
    [InlineData("{\"title\":5}", "title", "Use a title of up to 150 characters.")]
    [InlineData("{\"price\":-1}", "price", "Use a price above 0.")]
    [InlineData("{\"price\":\"cheap\"}", "price", "Use a price above 0.")]
    [InlineData("{\"price\":10,\"currency\":\"rupees\"}", "currency", "Use a three-letter currency code, such as INR.")]
    [InlineData("{\"currency\":\"INR\"}", "currency", "Send the currency with a price.")]
    [InlineData("{\"link\":\"acme.example/p/1\"}", "link", "Use a full web address starting with http:// or https://.")]
    public async Task Refuses_a_bad_fix(string json, string field, string message)
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        using var request = new HttpRequestMessage(HttpMethod.Patch, Fixes(brandId, productId))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        var response = await admin.Browser.SendAsync(request, Token);

        Assert.Equal(new[] { message }, await Catalogues.ErrorsAsync(response, field));
        Assert.Empty(Names(await GetAsync(admin.Browser, brandId, productId), "fixed"));
    }

    [Fact]
    public async Task A_title_over_150_characters_is_refused()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = new string('t', 151) }, Token);

        Assert.Equal(new[] { "Use a title of up to 150 characters." }, await Catalogues.ErrorsAsync(response, "title"));
    }

    [Theory]
    [InlineData("marketer", HttpStatusCode.OK)]
    [InlineData("approver", HttpStatusCode.Forbidden)]
    public async Task Admins_and_marketers_can_fix_but_approvers_cannot(string role, HttpStatusCode expected)
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var response = await member.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Trail Runner" }, Token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_fix_while_an_import_is_running_is_refused()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        HttpResponseMessage response;
        await using (await CatalogueLocks.HoldAsync(ConnectionString, brandId))
        {
            response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Trail Runner" }, Token);
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Kairo is already importing this catalogue. Try again in a minute.", await Catalogues.ProblemTitleAsync(response));
    }

    [Fact]
    public async Task An_unknown_product_cant_be_fixed()
    {
        var (admin, brandId, _) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, Guid.NewGuid()), new { title = "Trail Runner" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Fixes(Guid brandId, Guid productId) => $"{Catalogues.Base(brandId)}/products/{productId}/fixes";

    private static string[] Names(JsonElement product, string list) =>
        product.GetProperty(list).EnumerateArray().Select(name => name.GetString()!).ToArray();

    private static async Task<JsonElement> FixAsync(HttpClient browser, Guid brandId, Guid productId, object fixes)
    {
        var response = await browser.PatchAsJsonAsync(Fixes(brandId, productId), fixes, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId, Guid productId) =>
        (await browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/products/{productId}", Token));

    private async Task<(Member Admin, Guid BrandId, Guid ProductId)> ProductAsync(string row)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(row));
        var productId = Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Id;
        return (admin, brandId, productId);
    }
}
