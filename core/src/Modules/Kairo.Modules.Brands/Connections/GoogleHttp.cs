using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.Modules.Brands.Connections;

internal static class GoogleHttp
{
    public const string ClientName = "kairo-google";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<HttpResponseMessage> CallAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        object? body,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return await client.SendAsync(request, cancellationToken);
    }
}
