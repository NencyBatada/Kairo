using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kairo.IntegrationTests.Api;

public sealed record GoogleUser(string Subject, string Email, string Name, bool EmailVerified = true)
{
    public static GoogleUser Create(string name = "Test User", bool emailVerified = true)
    {
        var id = Guid.NewGuid().ToString("N");
        return new GoogleUser(id, $"user-{id}@example.com", name, emailVerified);
    }
}

public sealed class FakeGoogle
{
    public const string ClientId = "kairo-test-client";
    public const string Issuer = "https://accounts.google.com";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "kairo-test-key" };
    private readonly ConcurrentDictionary<string, (GoogleUser User, string Nonce)> _codes = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> TokenRequests { get; } = new();

    public void Configure(OpenIdConnectOptions options)
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
        };
        configuration.SigningKeys.Add(_key);
        options.Configuration = configuration;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        options.Backchannel = new HttpClient(new TokenEndpoint(this));
    }

    public string IssueCode(GoogleUser user, string nonce)
    {
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = (user, nonce);
        return code;
    }

    private string CreateIdToken(GoogleUser user, string nonce)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Subject,
                ["email"] = user.Email,
                ["email_verified"] = user.EmailVerified,
                ["name"] = user.Name,
                ["nonce"] = nonce,
            },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class TokenEndpoint(FakeGoogle google) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
            google.TokenRequests.Enqueue(form);

            if (!google._codes.TryRemove(form["code"], out var issued))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Dictionary<string, object>
                {
                    ["access_token"] = "fake-access-token",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 3600,
                    ["id_token"] = google.CreateIdToken(issued.User, issued.Nonce),
                }),
            };
        }
    }
}
