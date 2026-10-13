using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;

namespace Kairo.IntegrationTests.Api;

public sealed record IssuedCode(string Code, string RefreshToken, string Subject);

public sealed class FakeGoogleApis : HttpMessageHandler
{
    public const string ClientSecret = "kairo-test-secret";

    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly ConcurrentDictionary<string, string> _refreshTokens = new();
    private readonly ConcurrentDictionary<string, bool> _accessTokens = new();

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> Revoked { get; } = new();

    public bool RevocationFails { get; set; }

    public HttpStatusCode AdsWrite { get; set; } = HttpStatusCode.OK;

    public string[] YouTubeChannels { get; set; } = ["UC-acme"];

    public string[] MerchantAccessRights { get; set; } = ["ADMIN"];

    public HttpStatusCode Ga4Read { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryRead { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryJobs { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode GmailSend { get; set; } = HttpStatusCode.OK;

    public ConcurrentQueue<string> GmailMessages { get; } = new();

    public IReadOnlyList<MimeMessage> SentMail =>
        GmailMessages.Select(raw => MimeMessage.Load(new MemoryStream(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/'))))).ToList();

    public int Refreshes => Requests.Count(request => request == "refresh_token");

    public IssuedCode IssueCode(string challenge, string redirectUri, IEnumerable<string> scopes, string subject)
    {
        var issued = new IssuedCode(Guid.NewGuid().ToString("N"), $"refresh-{Guid.NewGuid():N}", subject);
        _codes[issued.Code] = new PendingCode(issued, challenge, redirectUri, string.Join(' ', scopes.Append("openid").Distinct()));
        return issued;
    }

    public void Reject(string refreshToken) => _refreshTokens.TryRemove(refreshToken, out _);

    public void AllowRefreshToken(string refreshToken) => _refreshTokens[refreshToken] = "sender";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        if (uri.Host == "oauth2.googleapis.com")
        {
            var form = QueryHelpers.ParseQuery("?" + body).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            return uri.AbsolutePath == "/revoke" ? Revoke(form) : Token(form);
        }

        Requests.Enqueue($"{request.Method} {uri.Host}{uri.AbsolutePath}");
        if (request.Headers.Authorization?.Parameter is not { } accessToken || !_accessTokens.ContainsKey(accessToken))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var login = request.Headers.TryGetValues("login-customer-id", out var values) ? values.Single() : null;
        return Product(uri, body, login);
    }

    private HttpResponseMessage Token(Dictionary<string, string> form)
    {
        if (form.GetValueOrDefault("client_id") != FakeGoogle.ClientId || form.GetValueOrDefault("client_secret") != ClientSecret)
        {
            return Error(HttpStatusCode.Unauthorized, "invalid_client");
        }

        if (form.GetValueOrDefault("grant_type") == "refresh_token")
        {
            Requests.Enqueue("refresh_token");
            return _refreshTokens.ContainsKey(form.GetValueOrDefault("refresh_token") ?? "")
                ? Tokens(null, null, null)
                : Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        Requests.Enqueue("authorization_code");
        if (!_codes.TryRemove(form.GetValueOrDefault("code") ?? "", out var pending)
            || pending.RedirectUri != form.GetValueOrDefault("redirect_uri")
            || pending.Challenge != Challenge(form.GetValueOrDefault("code_verifier") ?? ""))
        {
            return Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        _refreshTokens[pending.Issued.RefreshToken] = pending.Issued.Subject;
        return Tokens(pending.Issued.RefreshToken, pending.Scopes, pending.Issued.Subject);
    }

    private HttpResponseMessage Tokens(string? refreshToken, string? scope, string? subject)
    {
        var accessToken = $"access-{Guid.NewGuid():N}";
        _accessTokens[accessToken] = true;
        var response = new Dictionary<string, object> { ["access_token"] = accessToken, ["token_type"] = "Bearer", ["expires_in"] = 3600 };
        if (refreshToken is not null)
        {
            response["refresh_token"] = refreshToken;
        }

        if (scope is not null)
        {
            response["scope"] = scope;
        }

        if (subject is not null)
        {
            response["id_token"] = string.Join('.', Encode(new { alg = "none" }), Encode(new { sub = subject }), "signature");
        }

        return Json(HttpStatusCode.OK, response);
    }

    private HttpResponseMessage Revoke(Dictionary<string, string> form)
    {
        if (RevocationFails)
        {
            return Error(HttpStatusCode.BadRequest, "invalid_token");
        }

        var token = form.GetValueOrDefault("token") ?? "";
        Revoked.Enqueue(token);
        _refreshTokens.TryRemove(token, out _);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private HttpResponseMessage Product(Uri uri, string body, string? login)
    {
        var path = uri.AbsolutePath;
        return (uri.Host, path) switch
        {
            ("googleads.googleapis.com", "/v25/customers:listAccessibleCustomers") =>
                Json(HttpStatusCode.OK, new { resourceNames = new[] { "customers/1111111111", "customers/2222222222", "customers/3333333333" } }),
            ("googleads.googleapis.com", _) when !AdsReachable(path, login) => new HttpResponseMessage(HttpStatusCode.Forbidden),
            ("googleads.googleapis.com", _) when path.EndsWith("/googleAds:search", StringComparison.Ordinal) =>
                body.Contains("FROM customer_client", StringComparison.Ordinal) ? AdsClients(path) : AdsCustomer(path),
            ("googleads.googleapis.com", _) when path.EndsWith("/campaignBudgets:mutate", StringComparison.Ordinal) =>
                body.Contains("\"validateOnly\":true", StringComparison.Ordinal) ? Json(AdsWrite, new { }) : new HttpResponseMessage(HttpStatusCode.BadRequest),
            ("www.googleapis.com", "/youtube/v3/channels") =>
                Json(HttpStatusCode.OK, new { items = YouTubeChannels.Select(id => new { id, snippet = new { title = $"Channel {id}" } }) }),
            ("merchantapi.googleapis.com", "/accounts/v1/accounts") =>
                Json(HttpStatusCode.OK, new { accounts = new[] { new { accountId = "555", accountName = "Acme Store" } } }),
            ("merchantapi.googleapis.com", "/accounts/v1/accounts/555/users/me") =>
                Json(HttpStatusCode.OK, new { accessRights = MerchantAccessRights }),
            ("analyticsadmin.googleapis.com", "/v1beta/accountSummaries") =>
                Json(HttpStatusCode.OK, new { accountSummaries = new[] { new { displayName = "Acme", propertySummaries = new[] { new { property = "properties/777", displayName = "Acme web" } } } } }),
            ("analyticsadmin.googleapis.com", "/v1beta/properties/777") => Json(Ga4Read, new { name = "properties/777" }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects") =>
                Json(HttpStatusCode.OK, new { projects = new[] { new { id = "acme-data", friendlyName = "Acme data" } } }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects/acme-data/datasets") => Json(BigQueryRead, new { datasets = Array.Empty<object>() }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects/acme-data/jobs") => Json(BigQueryJobs, new { }),
            ("gmail.googleapis.com", "/gmail/v1/users/me/messages/send") => SendMail(body),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage SendMail(string body)
    {
        if (GmailSend != HttpStatusCode.OK)
        {
            return Json(GmailSend, new { error = new { code = (int)GmailSend, message = "Refused by the fake." } });
        }

        using var request = JsonDocument.Parse(body);
        GmailMessages.Enqueue(request.RootElement.GetProperty("raw").GetString()!);
        return Json(HttpStatusCode.OK, new { id = $"message-{GmailMessages.Count}", threadId = "thread" });
    }

    private static bool AdsReachable(string path, string? login) =>
        path.Split('/') is not [_, _, _, "4444444444", ..] || login == "2222222222";

    private static HttpResponseMessage AdsClients(string path) =>
        path.Split('/')[3] == "2222222222"
            ? Json(HttpStatusCode.OK, new { results = new[] { new { customerClient = new { id = "4444444444", descriptiveName = "Agency Client Shoes", manager = false } } } })
            : Json(HttpStatusCode.OK, new { results = Array.Empty<object>() });

    private static HttpResponseMessage AdsCustomer(string path)
    {
        var id = path.Split('/')[3];
        object? customer = id switch
        {
            "1111111111" => new { id, descriptiveName = "Acme Shoes India", manager = false },
            "2222222222" => new { id, descriptiveName = "Acme Agency", manager = true },
            "3333333333" => new { id, descriptiveName = "Acme Outlet", manager = false },
            "4444444444" => new { id, descriptiveName = "Agency Client Shoes", manager = false },
            _ => null,
        };
        return customer is null
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Json(HttpStatusCode.OK, new { results = new[] { new { customer } } });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage Error(HttpStatusCode status, string error) => Json(status, new { error });

    private static string Challenge(string verifier) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Encode(object value) => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(value));

    private sealed record PendingCode(IssuedCode Issued, string Challenge, string RedirectUri, string Scopes);
}
