using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class ConnectionTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connections_are_covered_by_the_brands_security_policy()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'brands.Connections')";

        Assert.Equal(3, (int)(await command.ExecuteScalarAsync(Token))!);
    }

    [Fact]
    public async Task A_workspace_has_at_most_one_connection_per_product()
    {
        var brandId = Guid.NewGuid();
        await InsertAsync(brandId);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(brandId));

        Assert.Equal(2601, duplicate.Number);
    }

    private async Task InsertAsync(Guid brandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO brands.Connections (Id, BrandId, Product, State, Scopes, GoogleSubject, ConnectedBy, ConnectedByName, ConnectedAt)
            VALUES (NEWID(), @brand, N'GoogleAds', N'ChoosingAccount', N'', N'subject', NEWID(), N'Neha', SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        await command.ExecuteNonQueryAsync(Token);
    }
}
