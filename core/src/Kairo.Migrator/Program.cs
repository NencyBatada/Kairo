using Kairo.SharedKernel.Persistence;

namespace Kairo.Migrator;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        await using var host = MigratorHost.Build(args);
        await ModuleMigrations.ApplyAsync(host.Services, CancellationToken.None);
    }
}
