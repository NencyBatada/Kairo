using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class MeasurementTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("FeedRuns")]
    [InlineData("SalesUploads")]
    [InlineData("SalesSources")]
    public async Task The_table_is_covered_by_the_measurement_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'measurement.{table}')",
            Guid.Empty,
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task A_workspace_has_at_most_one_sales_source()
    {
        var brandId = Guid.NewGuid();
        await InsertSourceAsync(brandId);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertSourceAsync(brandId));

        Assert.Equal(2627, duplicate.Number);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_a_feed_run()
    {
        var brandId = Guid.NewGuid();
        await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO measurement.FeedRuns (Id, BrandId, Feed, Status, [Rows], StartedAt)
            VALUES (NEWID(), @brand, N'google-ads', N'succeeded', 0, SYSDATETIMEOFFSET());
            SELECT 0;
            """,
            brandId,
            Guid.Empty);

        var seen = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @other;
            SELECT COUNT(*) FROM measurement.FeedRuns WHERE BrandId = @brand;
            """,
            brandId,
            Guid.NewGuid());

        Assert.Equal(0, seen);
    }

    private Task InsertSourceAsync(Guid brandId) => ScalarAsync(
        """
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO measurement.SalesSources (BrandId, TableRef, DateColumn, RegionColumn, OrdersColumn, RevenueColumn, Location, SetBy, SetAt)
        VALUES (@brand, N'acme-data.shop.daily_orders', N'order_date', N'state', N'order_count', N'net_revenue', N'asia-south1', NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId,
        Guid.Empty);

    private async Task<int> ScalarAsync(string sql, Guid brandId, Guid otherBrandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@other", otherBrandId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
