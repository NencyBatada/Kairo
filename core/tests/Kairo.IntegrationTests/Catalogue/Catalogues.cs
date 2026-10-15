using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kairo.IntegrationTests.Catalogue;

internal static class Catalogues
{
    public const string Header = "id,title,link,image_link,price,availability,product_type\n";

    public static string Base(Guid brandId) => $"/api/brands/{brandId}/catalogue";

    public static string Imports(Guid brandId) => $"{Base(brandId)}/imports";

    public static string Row(string id, string availability = "in_stock", string? title = null, string price = "1499.00 INR", string category = "Shoes") =>
        $"{id},{title ?? $"Runner {id}"},https://acme.example/p/{id},https://acme.example/i/{id}.jpg,{price},{availability},{category}\n";

    public static string Csv(params string[] rows) => Header + string.Concat(rows);

    public static string Many(int count, string availability = "in_stock") =>
        Header + string.Concat(Enumerable.Range(1, count).Select(i => Row($"SKU-{i}", availability)));

    public static MultipartFormDataContent File(string text, string fileName = "products.csv", bool? allowLargeRemoval = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        if (allowLargeRemoval is { } allow)
        {
            content.Add(new StringContent(allow ? "true" : "false"), "allowLargeRemoval");
        }

        return content;
    }

    public static Task<HttpResponseMessage> PostFileAsync(HttpClient browser, Guid brandId, string text, bool? allowLargeRemoval = null) =>
        browser.PostAsync(Imports(brandId), File(text, allowLargeRemoval: allowLargeRemoval));

    public static async Task<JsonElement> ImportAsync(HttpClient admin, Guid brandId, string text, bool? allowLargeRemoval = null)
    {
        var response = await PostFileAsync(admin, brandId, text, allowLargeRemoval);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    public static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
}
