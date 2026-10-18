using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Agents;
using Kairo.SharedKernel.Files;
using Kairo.SharedKernel.Http;
using Kairo.SharedKernel.Persistence;
using Kairo.SharedKernel.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.SharedKernel;

public static class KairoCoreServices
{
    public static IServiceCollection AddKairoCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddTransient<IntegrationTelemetryHandler>();
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler<IntegrationTelemetryHandler>());
        services.AddScoped(provider => new KairoDatabase(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("kairo")
                ?? throw new InvalidOperationException("The 'kairo' connection string is missing.")));
        services.AddScoped<CurrentMember>();
        services.AddKairoFiles();
        services.AddSafeWebClient();
        services.AddKairoAgents();
        return services;
    }
}
