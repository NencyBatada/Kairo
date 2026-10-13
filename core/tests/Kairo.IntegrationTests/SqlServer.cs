using Kairo.IntegrationTests;
using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServer))]

namespace Kairo.IntegrationTests;

public sealed class SqlServer : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string KairoConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        KairoConnectionString = await CreateDatabaseAsync("kairo");
        await using var migrator = MigratorHost.Build([$"--ConnectionStrings:kairo={KairoConnectionString}"]);
        await ModuleMigrations.ApplyAsync(migrator.Services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
        return new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
    }
}
