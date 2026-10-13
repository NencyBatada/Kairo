using System.Net;
using System.Runtime.CompilerServices;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Api;

public sealed class KairoApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://kairo.test";

    private readonly ConditionalWeakTable<HttpClient, CookieContainer> _cookies = new();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public FakeGoogle Google { get; } = new();

    public FakeGoogleApis GoogleApis { get; } = new();

    public string? AnalyticsProject { get; init; } = FakeBigQuery.KairoProject;

    public HttpClient CreateBrowser(bool withRequestHeader = true)
    {
        var cookies = new CookieContainer();
        var browser = CreateDefaultClient(new Uri(PublicOrigin), new CookieContainerHandler(cookies));
        if (withRequestHeader)
        {
            browser.DefaultRequestHeaders.Add(KairoApi.RequestHeader, "1");
        }

        _cookies.Add(browser, cookies);
        return browser;
    }

    public string CookieHeader(HttpClient browser) =>
        _cookies.TryGetValue(browser, out var cookies) ? cookies.GetCookieHeader(new Uri(PublicOrigin)) : "";

    public async Task<HttpClient> SignInAsync(GoogleUser user)
    {
        var browser = CreateBrowser();
        var location = await SignInAsync(browser, user, "/");
        if (location != "/")
        {
            throw new InvalidOperationException($"Sign-in ended at {location}.");
        }

        return browser;
    }

    public async Task<string> SignInAsync(HttpClient browser, GoogleUser user, string? returnUrl) =>
        (await CompleteSignInAsync(browser, user, returnUrl)).Headers.Location!.OriginalString;

    public async Task<HttpResponseMessage> CompleteSignInAsync(HttpClient browser, GoogleUser user, string? returnUrl)
    {
        var challenge = await browser.GetAsync($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
        var authorize = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var code = Google.IssueCode(user, authorize["nonce"]!);
        var callbackPath = new Uri(authorize["redirect_uri"]!).AbsolutePath;
        return await browser.GetAsync($"{callbackPath}?code={code}&state={Uri.EscapeDataString(authorize["state"]!)}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", connectionString);
        builder.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false,connectRetry=0,connectTimeout=1000");
        builder.UseSetting("Authentication:Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("Authentication:Google:ClientSecret", FakeGoogleApis.ClientSecret);
        builder.UseSetting("Kairo:PublicOrigin", PublicOrigin);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        if (AnalyticsProject is not null)
        {
            builder.UseSetting("Kairo:Analytics:Project", AnalyticsProject);
        }

        builder.UseSetting("Kairo:Notifications:DeliveryLoop", "off");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigureAll<OpenIdConnectOptions>(Google.Configure);
            services.AddHttpClient("kairo-google").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
            services.AddHttpClient("kairo-gmail").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
            services.AddHttpClient("kairo-measurement-google").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
            services.AddSingleton<IServiceAccessToken, FakeServiceToken>();
            configureServices?.Invoke(services);
        });
    }
}
