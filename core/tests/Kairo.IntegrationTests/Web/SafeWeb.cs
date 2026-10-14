using Kairo.SharedKernel.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Web;

internal static class SafeWeb
{
    public const string PublicOrigin = "https://kairo.test";

    public static SafeWebClient Client(HttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kairo:PublicOrigin"] = PublicOrigin })
            .Build());
        services.AddSafeWebClient();
        if (handler is not null)
        {
            services.AddHttpClient(SafeWebClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider().GetRequiredService<SafeWebClient>();
    }
}
