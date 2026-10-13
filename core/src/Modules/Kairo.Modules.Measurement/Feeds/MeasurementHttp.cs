using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.Modules.Measurement.Feeds;

internal static class MeasurementHttp
{
    public const string ClientName = "kairo-measurement-google";

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> RequestAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        HttpContent? content,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> CallAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        object? body,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        using var response = await client.RequestAsync(
            method, url, accessToken, body is null ? null : JsonContent.Create(body, options: Json), cancellationToken, headers);
        return await ReadAsync(response, cancellationToken);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleApiException(response.StatusCode, text);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.Clone();
    }
}

internal sealed class GoogleApiException(HttpStatusCode status, string body)
    : Exception($"Google answered {(int)status}: {MessageOf(body)}")
{
    public HttpStatusCode Status { get; } = status;

    private static string MessageOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            return Shorten(body);
        }

        return Shorten(body);
    }

    private static string Shorten(string body) => body.Length > 300 ? body[..300] : body;
}
