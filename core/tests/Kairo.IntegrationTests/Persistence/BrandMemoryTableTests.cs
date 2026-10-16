using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class BrandMemoryTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("MemoryEntries")]
    [InlineData("MemoryUses")]
    [InlineData("LearnRuns")]
    public async Task The_table_is_covered_by_the_brand_memory_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'brand_memory.{table}')",
            Guid.Empty,
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task An_entry_is_recorded_once_for_each_thing_that_used_it()
    {
        var brandId = Guid.NewGuid();
        var (entry, target) = (Guid.NewGuid(), Guid.NewGuid());
        await InsertUseAsync(brandId, entry, target);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertUseAsync(brandId, entry, target));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_a_memory_entry()
    {
        var brandId = Guid.NewGuid();
        await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO brand_memory.MemoryEntries (Id, BrandId, Kind, Outcome, Title, Lesson, Evidence, Products, Categories, Regions, Languages, State, CreatedBy, CreatedAt, Version)
            VALUES (NEWID(), @brand, N'message', N'worked', N'Quick-dry', N'Quick-dry claims worked.', N'{}', N'[]', N'[]', N'[]', N'[]', N'active', N'learn', SYSDATETIMEOFFSET(), 1);
            SELECT 0;
            """,
            brandId,
            Guid.Empty);

        var seen = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @other;
            SELECT COUNT(*) FROM brand_memory.MemoryEntries WHERE BrandId = @brand;
            """,
            brandId,
            Guid.NewGuid());

        Assert.Equal(0, seen);
    }

    private async Task InsertUseAsync(Guid brandId, Guid entryId, Guid targetId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO brand_memory.MemoryUses (Id, BrandId, EntryId, UsedIn, TargetId, UsedAt)
            VALUES (NEWID(), @brand, @entry, N'brief', @target, SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@entry", entryId);
        command.Parameters.AddWithValue("@target", targetId);
        await command.ExecuteNonQueryAsync(Token);
    }

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
