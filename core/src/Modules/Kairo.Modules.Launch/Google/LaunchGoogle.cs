using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Launch.Google;

internal sealed class LaunchException(string code, string message, string fix, bool transient = false, bool uncertain = false) : Exception(message)
{
    public string Code { get; } = code;
    public string Fix { get; } = fix;
    public bool Transient { get; } = transient;
    public bool Uncertain { get; } = uncertain;
}

internal sealed class LaunchGoogle(IHttpClientFactory http, IConnections connections, IConfiguration configuration)
{
    public const string ClientName = "kairo-launch-google";
    public string Version => configuration["Kairo:Google:AdsApiVersion"] ?? "v25";

    public async Task<JsonElement[]> SearchAsync(string account, string query, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ConnectedProduct.GoogleAds, account, HttpMethod.Post,
            $"https://googleads.googleapis.com/{Version}/customers/{account}/googleAds:search", JsonContent.Create(new { query }), false, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var rows = body.TryGetProperty("results", out var results) ? results.EnumerateArray().Select(row => row.Clone()).ToArray() : [];
        if (body.TryGetProperty("nextPageToken", out var token) && !string.IsNullOrEmpty(token.GetString()))
        {
            throw new LaunchException("too_many_results", "The launch lookup returned more results than can be checked safely.", "Narrow the campaign selection and try again.");
        }

        return rows;
    }

    public async Task<string[]> MutateAsync(string account, object[] operations, bool validateOnly, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ConnectedProduct.GoogleAds, account, HttpMethod.Post,
            $"https://googleads.googleapis.com/{Version}/customers/{account}/googleAds:mutate",
            JsonContent.Create(new { mutateOperations = operations, partialFailure = false, validateOnly }), !validateOnly, cancellationToken);
        if (validateOnly) return [];
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!result.TryGetProperty("mutateOperationResponses", out var rows))
        {
            throw new LaunchException("invalid_response", "Google didn't confirm the created resources.", "Reconcile this launch before retrying.", uncertain: true);
        }

        var names = rows.EnumerateArray().Select(row => row.EnumerateObject().First().Value.GetProperty("resourceName").GetString()!).ToArray();
        if (names.Length != operations.Length || names.Any(string.IsNullOrEmpty))
        {
            throw new LaunchException("invalid_response", "Google returned an incomplete resource list.", "Reconcile this launch before retrying.", uncertain: true);
        }

        return names;
    }

    public async Task<HttpResponseMessage> SendAsync(ConnectedProduct product, string account, HttpMethod method, string url,
        HttpContent? content, bool write, CancellationToken cancellationToken, Action<HttpRequestMessage>? configure = null)
    {
        var access = await connections.GetAccessAsync(product, cancellationToken);
        if (access.ExternalId != account) throw new LaunchException("account_changed", "The connected account changed after approval.", "Reconnect the approved account before retrying.");
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        if (product == ConnectedProduct.GoogleAds && access.ManagerId is { } manager) request.Headers.Add("login-customer-id", manager);
        configure?.Invoke(request);
        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new LaunchException("connection_interrupted", "Google's response was interrupted.", "Kairo must check whether the operation succeeded before retrying.", transient: true, uncertain: write);
        }

        if (response.IsSuccessStatusCode || (int)response.StatusCode == 308) return response;
        var status = response.StatusCode;
        string? reason = null;
        if (status == HttpStatusCode.BadRequest)
        {
            try
            {
                var failure = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                if (failure.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details))
                    foreach (var detail in details.EnumerateArray())
                        if (detail.TryGetProperty("errors", out var errors))
                            foreach (var item in errors.EnumerateArray())
                                if (item.TryGetProperty("errorCode", out var code)) reason ??= code.EnumerateObject().FirstOrDefault().Value.ToString();
            }
            catch (JsonException) { }
        }
        response.Dispose();
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new LaunchException("access_denied", "Google refused access to the approved account.", "Reconnect the account with permission to manage campaigns.");
        }

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
        {
            throw new LaunchException("google_unavailable", "Google is busy or temporarily unavailable.", "Try again after the retry delay.", transient: true, uncertain: write && status != HttpStatusCode.TooManyRequests);
        }

        var safeReason = reason is { Length: > 0 and <= 100 } && reason.All(character => char.IsAsciiLetterUpper(character) || character == '_') ? reason.Replace('_', ' ').ToLowerInvariant() : null;
        throw new LaunchException("google_validation", safeReason is null ? "Google rejected this launch step." : "Google rejected this launch step: " + safeReason + ".", "Check the account links, targeting, bids and approved creative against Google Ads requirements.");
    }
}
