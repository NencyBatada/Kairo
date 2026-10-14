using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Modules.Measurement.Feeds;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;

namespace Kairo.IntegrationTests.Api;

public sealed record IssuedCode(string Code, string RefreshToken, string Subject);

public sealed record AdsReport(string Customer, string? Login, string Query);

public sealed class FakeGoogleApis : HttpMessageHandler
{
    public const string ClientSecret = "kairo-test-secret";
    public const string ServiceToken = "kairo-service-account-token";

    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly ConcurrentDictionary<string, string> _refreshTokens = new();
    private readonly ConcurrentDictionary<string, bool> _accessTokens = new();
    private readonly object _adsReportsLock = new();
    private int _adsReportsInFlight;

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> Revoked { get; } = new();

    public FakeBigQuery BigQuery { get; } = new();

    public bool RevocationFails { get; set; }

    public HttpStatusCode AdsWrite { get; set; } = HttpStatusCode.OK;

    public string[] YouTubeChannels { get; set; } = ["UC-acme"];

    public string[] MerchantAccessRights { get; set; } = ["ADMIN"];

    public HttpStatusCode Ga4Read { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryRead { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryJobs { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode GmailSend { get; set; } = HttpStatusCode.OK;

    public string? GmailSendReason { get; set; }

    public ConcurrentQueue<string> GmailMessages { get; } = new();

    public IReadOnlyList<MimeMessage> SentMail =>
        GmailMessages.Select(raw => MimeMessage.Load(new MemoryStream(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/'))))).ToList();

    public string AdsTimeZone { get; set; } = "Asia/Kolkata";

    public HashSet<string> AdsEmptyDays { get; } = [];

    public HashSet<string> AdsFailures { get; } = [];

    public string? AdsFailFrom { get; set; }

    public TimeSpan AdsReportDelay { get; set; }

    public int MostAdsReportsAtOnce { get; private set; }

    public ConcurrentQueue<AdsReport> AdsReports { get; } = new();

    public Action? BeforeAdsReport { get; set; }

    public string Ga4TimeZone { get; set; } = "Asia/Kolkata";

    public int Ga4PageRows { get; set; } = 100;

    public ConcurrentQueue<JsonElement> Ga4Requests { get; } = new();

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
        if (request.Headers.Authorization?.Parameter is not { } accessToken
            || (accessToken != ServiceToken && !_accessTokens.ContainsKey(accessToken)))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (uri.Host == "bigquery.googleapis.com" && BigQuery.Handle(request, body, accessToken) is { } answer)
        {
            return answer;
        }

        if (AdsReportDelay > TimeSpan.Zero && uri.AbsolutePath.EndsWith("/googleAds:searchStream", StringComparison.Ordinal))
        {
            lock (_adsReportsLock)
            {
                _adsReportsInFlight++;
                MostAdsReportsAtOnce = Math.Max(MostAdsReportsAtOnce, _adsReportsInFlight);
            }

            try
            {
                await Task.Delay(AdsReportDelay, cancellationToken);
            }
            finally
            {
                lock (_adsReportsLock)
                {
                    _adsReportsInFlight--;
                }
            }
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
            ("googleads.googleapis.com", _) when path.EndsWith("/googleAds:searchStream", StringComparison.Ordinal) => AdsStream(path, body, login),
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
            ("analyticsadmin.googleapis.com", "/v1beta/properties/777") =>
                Json(Ga4Read, new { name = "properties/777", timeZone = Ga4TimeZone, currencyCode = "INR" }),
            ("analyticsdata.googleapis.com", "/v1beta/properties/777:runReport") => Ga4Report(body),
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
            return Json(GmailSend, new
            {
                error = new
                {
                    code = (int)GmailSend,
                    message = "Refused by the fake.",
                    errors = GmailSendReason is null ? [] : new[] { new { domain = "usageLimits", reason = GmailSendReason } },
                },
            });
        }

        using var request = JsonDocument.Parse(body);
        GmailMessages.Enqueue(request.RootElement.GetProperty("raw").GetString()!);
        return Json(HttpStatusCode.OK, new { id = $"message-{GmailMessages.Count}", threadId = "thread" });
    }

    private HttpResponseMessage AdsStream(string path, string body, string? login)
    {
        var customer = path.Split('/')[3];
        var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;
        BeforeAdsReport?.Invoke();
        AdsReports.Enqueue(new AdsReport(customer, login, query));
        var range = Regex.Match(query, "segments.date BETWEEN '(?<from>[0-9-]+)'");
        if (AdsFailures.Contains(customer)
            || (AdsFailFrom is { } failFrom && range.Success && string.CompareOrdinal(range.Groups["from"].Value, failFrom) >= 0))
        {
            return Json(
                HttpStatusCode.InternalServerError,
                new[] { new { error = new { code = 500, message = "Internal error encountered.", status = "INTERNAL" } } });
        }

        object[] results;
        if (query.EndsWith("FROM customer", StringComparison.Ordinal))
        {
            results = [new { customer = new { resourceName = $"customers/{customer}", id = customer, timeZone = AdsTimeZone, currencyCode = "INR" } }];
        }
        else if (query.Contains("FROM geo_target_constant", StringComparison.Ordinal))
        {
            results = [new { geoTargetConstant = new { resourceName = "geoTargetConstants/21342", id = "21342", name = "Maharashtra", countryCode = "IN" } }];
        }
        else
        {
            results = AdsDays(query).Select(date => AdsResult(query, date)).ToArray();
        }

        return Json(HttpStatusCode.OK, new[] { new { results, requestId = "fake-request" } });
    }

    private IEnumerable<string> AdsDays(string query)
    {
        var range = Regex.Match(query, "segments.date BETWEEN '(?<from>[0-9-]+)' AND '(?<to>[0-9-]+)'");
        var from = DateOnly.ParseExact(range.Groups["from"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(range.Groups["to"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!AdsEmptyDays.Contains(date))
            {
                yield return date;
            }
        }
    }

    private static object AdsResult(string query, string date)
    {
        var metrics = new { impressions = "1000", clicks = "50", costMicros = "2500000", conversions = 3.0, conversionsValue = 4500.5 };
        if (query.Contains("FROM geographic_view", StringComparison.Ordinal))
        {
            return new Dictionary<string, object>
            {
                ["segments"] = new Dictionary<string, object> { ["date"] = date, [GoogleAdsReports.RegionJson] = "geoTargetConstants/21342" },
                ["campaign"] = new { id = "9001" },
                ["metrics"] = metrics,
            };
        }

        if (query.Contains("FROM ad_group_ad", StringComparison.Ordinal))
        {
            return new
            {
                segments = new { date },
                campaign = new { id = "9001" },
                adGroup = new { id = "8001" },
                adGroupAd = new { ad = new { id = "7001", type = "RESPONSIVE_SEARCH_AD" } },
                metrics,
            };
        }

        if (query.Contains("FROM ad_group", StringComparison.Ordinal))
        {
            return new { segments = new { date }, campaign = new { id = "9001" }, adGroup = new { id = "8001", name = "Running shoes" }, metrics };
        }

        return new
        {
            segments = new { date },
            campaign = new { id = "9001", name = "Diwali Search", advertisingChannelType = "SEARCH", status = "ENABLED" },
            metrics,
        };
    }

    private HttpResponseMessage Ga4Report(string body)
    {
        var request = JsonDocument.Parse(body).RootElement.Clone();
        Ga4Requests.Enqueue(request);
        var range = request.GetProperty("dateRanges")[0];
        var from = DateOnly.ParseExact(range.GetProperty("startDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(range.GetProperty("endDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = new List<object>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            rows.Add(Ga4Row(date, "Maharashtra", "120", "4", "3", "5600.75"));
            if (day == from)
            {
                rows.Add(Ga4Row(date, "(not set)", "7", "0", "0", "0"));
            }
        }

        var offset = request.TryGetProperty("offset", out var start) ? start.GetInt32() : 0;
        return Json(HttpStatusCode.OK, new { rows = rows.Skip(offset).Take(Ga4PageRows).ToArray(), rowCount = rows.Count });
    }

    private static object Ga4Row(string date, string region, params string[] metrics) => new
    {
        dimensionValues = new[] { new { value = date }, new { value = "India" }, new { value = region } },
        metricValues = metrics.Select(value => new { value }).ToArray(),
    };

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
