extern alias worker;

using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Web;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Agents;
using Kairo.SharedKernel.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Api;

public sealed class KairoWorkerFactory(string connectionString, FakeGoogleApis googleApis, DateTimeOffset now)
    : WebApplicationFactory<worker::Program>
{
    public FakeTimeProvider Clock { get; } = new(now);

    public string? AnalyticsProject { get; init; } = FakeBigQuery.KairoProject;

    public FakeWebsites Websites { get; } = new();

    public FakeAgents Agents { get; } = new();

    public async Task<HttpResponseMessage> RunDailyAsync(Guid? brandId = null)
    {
        using var client = CreateClient();
        return await client.PostAsync(brandId is { } id ? $"/jobs/analytics/daily?brandId={id}" : "/jobs/analytics/daily", null);
    }

    public async Task<HttpResponseMessage> RefreshCatalogueAsync(Guid? brandId = null)
    {
        using var client = CreateClient();
        return await client.PostAsync(brandId is { } id ? $"/jobs/catalogue/refresh?brandId={id}" : "/jobs/catalogue/refresh", null);
    }

    public async Task<HttpResponseMessage> RefreshOpportunitiesAsync(Guid? brandId = null)
    {
        using var client = CreateClient();
        return await client.PostAsync(brandId is { } id ? $"/jobs/opportunities/refresh?brandId={id}" : "/jobs/opportunities/refresh", null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", connectionString);
        builder.UseSetting("Authentication:Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("Authentication:Google:ClientSecret", FakeGoogleApis.ClientSecret);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting(AgentClient.UrlSetting, "http://agents.test");
        if (AnalyticsProject is not null)
        {
            builder.UseSetting("Kairo:Analytics:Project", AnalyticsProject);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient("kairo-google").ConfigurePrimaryHttpMessageHandler(() => googleApis);
            services.AddHttpClient("kairo-measurement-google").ConfigurePrimaryHttpMessageHandler(() => googleApis);
            services.AddHttpClient("kairo-opportunities-google").ConfigurePrimaryHttpMessageHandler(() => googleApis);
            services.AddHttpClient(AgentClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => Agents);
            services.AddHttpClient(SafeWebClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => Websites);
            services.AddSingleton<IServiceAccessToken, FakeServiceToken>();
        });
    }
}
