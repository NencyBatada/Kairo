using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Live;

internal sealed record OutboxRow(long Id, string Type, string Payload, string AudienceKind, int AudienceRoles, string? AudienceUsers);

internal static class OutboxRows
{
    public static async Task<IReadOnlyList<OutboxRow>> ReadAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenForBrandAsync(connectionString, brandId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Type, Payload, AudienceKind, AudienceRoles, AudienceUsers
            FROM live.Outbox
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<OutboxRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new OutboxRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    public static async Task InsertAsync(string connectionString, Guid brandId, string type, int count, DateTimeOffset createdAt)
    {
        await using var connection = await OpenForBrandAsync(connectionString, brandId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO live.Outbox (BrandId, Type, Payload, AudienceKind, AudienceRoles, AudienceUsers, CreatedAt)
            SELECT TOP (@count) @brand, @type, N'{}', N'Everyone', 0, NULL, @createdAt
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
            """;
        command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqlConnection> OpenForBrandAsync(string connectionString, Guid brandId)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = connection.CreateCommand();
        context.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand;";
        context.Parameters.AddWithValue("@brand", brandId);
        await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
