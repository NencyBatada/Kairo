using Kairo.Migrator;
using Kairo.SharedKernel;
using Kairo.SharedKernel.Persistence;
using Kairo.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kairo.IntegrationTests;

public sealed class MigratorTests(SqlServer sqlServer)
{
    [Fact]
    public async Task The_migrator_registers_every_module()
    {
        await using var host = MigratorHost.Build([]);

        var registered = host.Services.GetServices<IModule>()
            .Select(module => module.GetType().Assembly.GetName().Name!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ModuleFolders.Names, registered);
    }

    [Fact]
    public async Task Every_registration_resolves_in_development()
    {
        await using var host = MigratorHost.Build(["--environment", "Development"]);

        Assert.True(host.Environment.IsDevelopment());
    }

    [Fact]
    public async Task Migrating_a_database_twice_is_harmless()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("migrate");

        for (var run = 0; run < 2; run++)
        {
            await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={connectionString}"]);
            await ModuleMigrations.ApplyAsync(host.Services, TestContext.Current.CancellationToken);
        }
    }
}
