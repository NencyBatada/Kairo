using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Live;

internal static class IsolatedDatabase
{
    public static async Task<string> CreateAsync(SqlServer sqlServer)
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("live");
        await using var migrator = MigratorHost.Build([$"--ConnectionStrings:kairo={connectionString}"]);
        await ModuleMigrations.ApplyAsync(migrator.Services, TestContext.Current.CancellationToken);
        return connectionString;
    }
}
