using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class CatalogueTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Products")]
    [InlineData("Sources")]
    [InlineData("Imports")]
    [InlineData("StockEvents")]
    public async Task The_table_is_covered_by_the_catalogue_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'catalogue.{table}')",
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task Offer_ids_are_unique_in_a_workspace_whatever_their_case()
    {
        var brandId = Guid.NewGuid();
        await InsertProductAsync(brandId, "SKU-1");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertProductAsync(brandId, "sku-1"));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task Two_workspaces_can_use_the_same_offer_id()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await InsertProductAsync(first, "SKU-1");
        await InsertProductAsync(second, "SKU-1");

        var count = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM catalogue.Products WHERE OfferId = N'SKU-1' AND BrandId IN (@brand, @other);
            """,
            first,
            second);

        Assert.Equal(2, count);
    }

    private Task InsertProductAsync(Guid brandId, string offerId) => ScalarAsync(
        """
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO catalogue.Products (Id, BrandId, OfferId, AdditionalImageLinks, Fixes, Missing, Status, FirstSeenAt, UpdatedAt, LastImportId)
        VALUES (NEWID(), @brand, @offer, N'[]', N'{}', 0, N'active', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID());
        SELECT 0;
        """,
        brandId,
        offer: offerId);

    private async Task<int> ScalarAsync(string sql, Guid brandId, Guid? other = null, string? offer = null)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@other", other ?? Guid.Empty);
        command.Parameters.AddWithValue("@offer", offer ?? "");
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
