using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.Modules.Opportunities.Refresh;

internal static class AdsHttp
{
    public const string ClientName = "kairo-opportunities-google";

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] AccessLostCodes = ["USER_PERMISSION_DENIED", "CUSTOMER_NOT_ENABLED"];

    public static async Task<JsonElement> PostAsync(
        this HttpClient client,
        string url,
        string accessToken,
        string? loginCustomerId,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (loginCustomerId is not null)
        {
            request.Headers.Add("login-customer-id", loginCustomerId);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AdsCallException(response.StatusCode, text, AccessLost(text));
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.Clone();
    }

    public static bool AccessLost(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0
                ? document.RootElement[0]
                : document.RootElement;
            if (!root.TryGetProperty("error", out var error) || !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return details.EnumerateArray()
                .Where(detail => detail.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                .SelectMany(detail => detail.GetProperty("errors").EnumerateArray())
                .Any(failure => failure.TryGetProperty("errorCode", out var code)
                    && code.ValueKind == JsonValueKind.Object
                    && code.TryGetProperty("authorizationError", out var authorization)
                    && AccessLostCodes.Contains(authorization.GetString()));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    public static long Number(JsonElement element, string name) =>
        long.TryParse(Text(element, name), out var number) ? number : 0;
}

internal sealed class AdsCallException(HttpStatusCode status, string body, bool accessLost)
    : Exception($"Google Ads answered {(int)status}: {(body.Length > 300 ? body[..300] : body)}")
{
    public bool AccessLost { get; } = accessLost;
}
