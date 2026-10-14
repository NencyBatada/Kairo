using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class BrandKitTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("KitVersions")]
    [InlineData("KitFiles")]
    public async Task The_table_is_covered_by_the_brand_kit_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'brand_kit.{table}')",
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Approved")]
    public async Task A_workspace_has_at_most_one_version_in_this_status(string status)
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, status);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertVersionAsync(brandId, 2, status));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task Superseded_and_discarded_versions_can_pile_up()
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, "Superseded");
        await InsertVersionAsync(brandId, 2, "Superseded");
        await InsertVersionAsync(brandId, 3, "Discarded");
        await InsertVersionAsync(brandId, 4, "Discarded");

        var count = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM brand_kit.KitVersions WHERE BrandId = @brand;
            """,
            brandId);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Version_numbers_are_unique_in_a_workspace()
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, "Superseded");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertVersionAsync(brandId, 1, "Discarded"));

        Assert.Equal(2601, duplicate.Number);
    }

    private Task InsertVersionAsync(Guid brandId, int number, string status) => ScalarAsync(
        $$"""
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO brand_kit.KitVersions (Id, BrandId, Number, Status, Content, Revision, Origin, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
        VALUES (NEWID(), @brand, {{number}}, N'{{status}}', N'{}', 1, N'Manual', NEWID(), SYSDATETIMEOFFSET(), NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId);

    private async Task<int> ScalarAsync(string sql, Guid brandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@brand", brandId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
