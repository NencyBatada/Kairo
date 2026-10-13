using Kairo.Contracts.Audit;
using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Audit;

public sealed class AuditStorageTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recording_an_entry_keeps_who_did_what_to_which_target()
    {
        var brandId = Guid.NewGuid();
        await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={sqlServer.KairoConnectionString}"]);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(
                new AuditRecord("test.recorded", "thing", "42", Before: new { Name = "Old" }, After: new { Name = "New" }),
                Token);
        }

        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand;
            SELECT [ActorType], [ActorName], [Action], [TargetType], [TargetId], [Before], [After] FROM [audit].[Entries];
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(
            ("system", "Kairo", "test.recorded", "thing", "42", """{"name":"Old"}""", """{"name":"New"}"""),
            (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        Assert.False(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task Recording_needs_a_brand()
    {
        await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={sqlServer.KairoConnectionString}"]);
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(new AuditRecord("test.recorded", "thing", "1"), Token));
    }

    [Fact]
    public async Task Audit_entries_cannot_be_changed_or_deleted()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);

        foreach (var statement in new[] { "UPDATE [audit].[Entries] SET [Action] = N'tampered'", "DELETE FROM [audit].[Entries]" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; {statement};";
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(Token));
            Assert.Contains("cannot be changed or deleted", error.Message);
        }
    }
}
