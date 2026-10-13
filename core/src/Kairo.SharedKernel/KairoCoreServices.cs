using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
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
        services.AddScoped(provider => new KairoDatabase(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("kairo")
                ?? throw new InvalidOperationException("The 'kairo' connection string is missing.")));
        services.AddScoped<CurrentMember>();
        return services;
    }
}
