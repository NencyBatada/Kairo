using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Brands.Connections;

internal sealed record OAuthClient(string Id, string Secret)
{
    public override string ToString() => Id;
}

internal sealed record Grant(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken, string[] Scopes, string? Subject);

internal sealed class GoogleOAuth(IHttpClientFactory http, IConfiguration configuration, TimeProvider time, ILogger<GoogleOAuth> logger)
{
    public const string CallbackPath = "/api/connections/callback/google";

    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private string ClientId => configuration["Authentication:Google:ClientId"]
        ?? throw new InvalidOperationException("Authentication:Google:ClientId is not set.");

    private string ClientSecret => configuration["Authentication:Google:ClientSecret"]
        ?? throw new InvalidOperationException("Authentication:Google:ClientSecret is not set.");

    public string RedirectUri(HttpRequest request)
    {
        var origin = (configuration["Kairo:PublicOrigin"] ?? "").TrimEnd('/');
        return (origin.Length > 0 ? origin : $"{request.Scheme}://{request.Host}") + CallbackPath;
    }

    public string AuthorizeUrl(ProductInfo product, ConsentState state, string protectedState, string redirectUri) =>
        QueryHelpers.AddQueryString(AuthorizeEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', product.Scopes.Prepend("openid")),
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
            ["include_granted_scopes"] = "false",
            ["code_challenge"] = state.Challenge(),
            ["code_challenge_method"] = "S256",
            ["state"] = protectedState,
        });

    public async Task<Grant?> ExchangeAsync(string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google refused an authorization code with status {Status}.", (int)response.StatusCode);
            return null;
        }

        return await ReadGrantAsync(response, cancellationToken);
    }

    public async Task<Grant?> RefreshAsync(string refreshToken, OAuthClient? client, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = client?.Id ?? ClientId,
            ["client_secret"] = client?.Secret ?? ClientSecret,
        }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest || (client is not null && response.StatusCode == HttpStatusCode.Unauthorized))
        {
            var error = await ErrorOfAsync(response, cancellationToken);
            if (error == "invalid_grant" || (client is not null && error is "invalid_client" or "unauthorized_client"))
            {
                return null;
            }
        }

        response.EnsureSuccessStatusCode();
        return await ReadGrantAsync(response, cancellationToken);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PostAsync(RevokeEndpoint, new Dictionary<string, string> { ["token"] = token }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google refused to revoke a token with status {Status}.", (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not revoke a Google token.");
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        return await http.CreateClient(GoogleHttp.ClientName).PostAsync(url, content, cancellationToken);
    }

    private async Task<Grant> ReadGrantAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(GoogleHttp.SnakeCase, cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty token response.");
        return new Grant(
            token.AccessToken,
            time.GetUtcNow().AddSeconds(token.ExpiresIn),
            token.RefreshToken,
            (token.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            SubjectOf(token.IdToken));
    }

    private static async Task<string?> ErrorOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<TokenError>(GoogleHttp.SnakeCase, cancellationToken))?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? SubjectOf(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
        return payload.RootElement.TryGetProperty("sub", out var subject) ? subject.GetString() : null;
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresIn, string? RefreshToken, string? Scope, string? IdToken);

    private sealed record TokenError(string? Error);
}
