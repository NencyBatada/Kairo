using Kairo.Modules.Catalogue.Imports;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Catalogue;

internal static class CatalogueLocks
{
    public static async Task<IAsyncDisposable> HoldAsync(string connectionString, Guid brandId)
    {
        var database = new KairoDatabase(connectionString);
        await database.BeginTransactionAsync(CancellationToken.None);
        Assert.True(await database.TryLockAsync(CatalogueImporter.LockName(brandId), CancellationToken.None));
        return database;
    }

    public static async Task<IAsyncDisposable> WriteWithoutCommittingAsync(string connectionString, Guid brandId, int products)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        var transaction = connection.BeginTransaction();
        await using var command = new SqlCommand(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            WITH numbers AS (SELECT TOP (@count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns a CROSS JOIN sys.all_columns b)
            INSERT INTO catalogue.Products (Id, BrandId, OfferId, AdditionalImageLinks, Fixes, Missing, Status, FirstSeenAt, UpdatedAt, LastImportId)
            SELECT NEWID(), @brand, CONCAT(N'HELD-', n), N'[]', N'{}', 0, N'active', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID() FROM numbers;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@count", products);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return new HeldWrite(connection, transaction);
    }

    private sealed class HeldWrite(SqlConnection connection, SqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}
