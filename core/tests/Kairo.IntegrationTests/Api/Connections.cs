using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Api;

public sealed record Callback(HttpResponseMessage Response, IssuedCode Issued, string Url)
{
    public string Location => Response.Headers.Location!.OriginalString;
}

internal static class Connections
{
    public static readonly Dictionary<string, string> StoredProducts = new()
    {
        ["google-ads"] = "GoogleAds",
        ["youtube"] = "YouTube",
        ["merchant-center"] = "MerchantCenter",
        ["ga4"] = "Ga4",
        ["bigquery"] = "BigQuery",
    };

    public static async Task<Uri> StartAsync(HttpClient admin, Guid brandId, string product)
    {
        var response = await admin.GetAsync($"/api/brands/{brandId}/connections/{product}/connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    public static async Task<Callback> CompleteAsync(
        KairoApiFactory api,
        HttpClient browser,
        Uri authorize,
        string? subject = null,
        IEnumerable<string>? scopes = null)
    {
        var query = QueryHelpers.ParseQuery(authorize.Query);
        var issued = api.GoogleApis.IssueCode(
            query["code_challenge"]!,
            query["redirect_uri"]!,
            scopes ?? query["scope"].ToString().Split(' '),
            subject ?? $"google-{Guid.NewGuid():N}");
        var url = $"/api/connections/callback/google?code={issued.Code}&state={Uri.EscapeDataString(query["state"]!)}";
        return new Callback(await browser.GetAsync(url), issued, url);
    }

    public static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId, string product)
    {
        var list = await browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections");
        return list.EnumerateArray().Single(connection => connection.GetProperty("product").GetString() == product);
    }

    public static async Task<byte[]?> StoredTokenAsync(string connectionString, Guid brandId, string product)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT RefreshToken FROM brands.Connections WHERE BrandId = @brand AND Product = @product;
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@product", StoredProducts[product]);
        return await command.ExecuteScalarAsync() as byte[];
    }

    public static string DefaultAccount(string product) => product switch
    {
        "google-ads" => "1111111111",
        "youtube" => "UC-acme",
        "merchant-center" => "555",
        "ga4" => "777",
        "bigquery" => "acme-data",
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
    };

    public static async Task<IssuedCode> ConnectAsync(KairoApiFactory api, HttpClient admin, Guid brandId, string product, string? subject = null)
    {
        var callback = await CompleteAsync(api, admin, await StartAsync(admin, brandId, product), subject);
        Assert.Contains($"choose={product}", callback.Location);
        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/{product}/account", new { externalId = DefaultAccount(product) });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        return callback.Issued;
    }
}
