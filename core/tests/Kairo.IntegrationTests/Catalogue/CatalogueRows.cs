using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Catalogue;

internal sealed record ProductRow(
    Guid Id,
    string OfferId,
    string? Title,
    string? Availability,
    string Status,
    DateTimeOffset? RemovedAt,
    int Missing,
    string Fixes,
    Guid LastImportId);

internal sealed record StockRow(Guid ProductId, string? From, string? To, Guid ImportId);

internal sealed record ImportRow(Guid Id, string Status, string? StorageKey, Guid? StartedBy);

internal static class CatalogueRows
{
    public static Task<List<ProductRow>> ProductsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        "SELECT Id, OfferId, Title, Availability, Status, RemovedAt, Missing, Fixes, LastImportId FROM catalogue.Products WHERE BrandId = @brand ORDER BY OfferId",
        reader => new ProductRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetGuid(8)));

    public static Task<List<StockRow>> StockEventsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        """
        SELECT e.ProductId, e.[From], e.[To], e.ImportId
        FROM catalogue.StockEvents e JOIN catalogue.Imports i ON i.Id = e.ImportId
        WHERE e.BrandId = @brand
        ORDER BY i.Number, e.ProductId
        """,
        reader => new StockRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetGuid(3)));

    public static Task<List<ImportRow>> ImportsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        "SELECT Id, Status, StorageKey, StartedBy FROM catalogue.Imports WHERE BrandId = @brand ORDER BY Number",
        reader => new ImportRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3)));

    private static async Task<List<T>> ReadAsync<T>(string connectionString, Guid brandId, string sql, Func<SqlDataReader, T> map)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;\n" + sql, connection);
        command.Parameters.AddWithValue("@brand", brandId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
