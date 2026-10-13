using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class SenderToken(IHttpClientFactory http, IConfiguration configuration, TimeProvider time, ILogger<SenderToken> logger)
{
    public const string ClientName = "kairo-gmail";

    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _expiresAt - Margin > time.GetUtcNow())
            {
                return _accessToken;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = configuration[NotificationSettings.SenderRefreshToken] ?? "",
                ["client_id"] = configuration["Authentication:Google:ClientId"] ?? "",
                ["client_secret"] = configuration["Authentication:Google:ClientSecret"] ?? "",
            });
            using var response = await http.CreateClient(ClientName).PostAsync(TokenEndpoint, content, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest
                && (await response.Content.ReadFromJsonAsync<TokenError>(SnakeCase, cancellationToken))?.Error == "invalid_grant")
            {
                logger.LogCritical("The notification sender mailbox needs signing in again. Run deploy/set-mail-sender.ps1.");
                throw new EmailSendException("The notification sender mailbox needs signing in again.", mayPass: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new EmailSendException($"Google's token endpoint answered {(int)response.StatusCode}.", mayPass: true);
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(SnakeCase, cancellationToken)
                ?? throw new EmailSendException("Google returned an empty token response.", mayPass: true);
            _accessToken = token.AccessToken;
            _expiresAt = time.GetUtcNow().AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Forget() => _accessToken = null;

    private sealed record TokenResponse(string AccessToken, int ExpiresIn);

    private sealed record TokenError(string? Error);
}
