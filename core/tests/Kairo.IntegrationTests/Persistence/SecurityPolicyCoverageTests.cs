using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class SecurityPolicyCoverageTests(SqlServer sqlServer)
{
    [Fact]
    public async Task Every_table_with_a_brand_column_has_row_level_security()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name + N'.' + t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE (EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'BrandId')
                   OR t.object_id = OBJECT_ID(N'brands.Brands'))
              AND NOT EXISTS (SELECT 1 FROM sys.security_predicates p WHERE p.target_object_id = t.object_id AND p.predicate_type_desc = N'FILTER')
            """;
        var uncovered = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            uncovered.Add(reader.GetString(0));
        }

        Assert.Empty(uncovered);
    }
}
