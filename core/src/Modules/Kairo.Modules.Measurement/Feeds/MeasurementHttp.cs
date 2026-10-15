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

    public bool AccessLost { get; } = GoogleAccessErrors.MeansAccessLost(status, body);

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

internal sealed class ConnectionAccessLostException(GoogleApiException inner) : Exception(inner.Message, inner);

internal static class GoogleAccessErrors
{
    private static readonly string[] AdsAccessLost = ["USER_PERMISSION_DENIED", "CUSTOMER_NOT_ENABLED"];

    private static readonly string[] NotAboutAccess =
    [
        "SERVICE_DISABLED", "BILLING_DISABLED", "API_DISABLED", "RATE_LIMIT_EXCEEDED", "RESOURCE_EXHAUSTED",
        "quotaExceeded", "rateLimitExceeded", "billingNotEnabled", "accessNotConfigured", "responseTooLarge",
    ];

    private static readonly string[] AccessReasons = ["accessDenied", "forbidden", "insufficientPermissions"];

    public static bool MeansAccessLost(HttpStatusCode status, string body)
    {
        if (ErrorOf(body) is not { } error)
        {
            return false;
        }

        var details = Array(error, "details");
        var adsFailures = details
            .Where(detail => Text(detail, "@type")?.Contains("GoogleAdsFailure", StringComparison.Ordinal) == true)
            .ToList();
        if (adsFailures.Count > 0)
        {
            return adsFailures
                .SelectMany(failure => Array(failure, "errors"))
                .Select(failure => failure.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.Object ? Text(code, "authorizationError") : null)
                .Any(code => code is not null && AdsAccessLost.Contains(code));
        }

        if (status != HttpStatusCode.Forbidden)
        {
            return false;
        }

        var reasons = details.Select(detail => Text(detail, "reason"))
            .Concat(Array(error, "errors").Select(item => Text(item, "reason")))
            .OfType<string>()
            .ToList();
        if (reasons.Any(reason => NotAboutAccess.Contains(reason)))
        {
            return false;
        }

        return Text(error, "status") == "PERMISSION_DENIED" || reasons.Any(reason => AccessReasons.Contains(reason));
    }

    private static JsonElement? ErrorOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? error.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal static class ConnectionCalls
{
    public static async Task<T> OnConnectionAsync<T>(this Task<T> call)
    {
        try
        {
            return await call;
        }
        catch (GoogleApiException exception) when (exception.AccessLost)
        {
            throw new ConnectionAccessLostException(exception);
        }
    }
}
