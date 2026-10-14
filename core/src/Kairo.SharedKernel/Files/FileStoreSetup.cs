using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.SharedKernel.Files;

public static class FileStoreSetup
{
    public const string HttpClientName = "kairo-files";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddKairoFiles(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout).RemoveAllResilienceHandlers();
        services.TryAddSingleton<FileStoreOptions>();
        services.TryAddSingleton<IStorageAccessToken, ApplicationDefaultStorageToken>();
        services.TryAddSingleton<IFileStore>(provider =>
        {
            var options = provider.GetRequiredService<FileStoreOptions>();
            return options.Project is null
                ? new DiskFileStore(options)
                : ActivatorUtilities.CreateInstance<GoogleCloudFileStore>(provider);
        });
        return services;
    }
}
