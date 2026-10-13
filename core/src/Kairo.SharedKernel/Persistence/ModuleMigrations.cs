using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel.Persistence;

public static class ModuleMigrations
{
    public static async Task ApplyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var module in services.GetServices<IModule>())
        {
            await using var scope = services.CreateAsyncScope();
            await module.MigrateAsync(scope.ServiceProvider, cancellationToken);
        }
    }
}
