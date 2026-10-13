using System.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Feeds;

public sealed record StoredRun(
    string Feed,
    string? ExternalId,
    string Status,
    string? Reason,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Rows,
    string? Error,
    DateTimeOffset StartedAt);

internal static class FeedRunRows
{
    public static async Task<List<StoredRun>> ListAsync(string connectionString, Guid brandId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT Feed, ExternalId, Status, Reason, FromDate, ToDate, [Rows], Error, StartedAt
            FROM measurement.FeedRuns
            WHERE BrandId = @brand
            ORDER BY StartedAt, Feed;
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var runs = new List<StoredRun>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(new StoredRun(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDateTimeOffset(8)));
        }

        return runs;
    }

    public static async Task InsertAsync(
        string connectionString,
        Guid brandId,
        string feed,
        string status,
        DateTimeOffset startedAt,
        DateOnly? toDate = null,
        string? error = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO measurement.FeedRuns (Id, BrandId, Feed, ExternalId, Status, FromDate, ToDate, [Rows], Error, StartedAt, FinishedAt)
            VALUES (NEWID(), @brand, @feed, N'seeded', @status, @to, @to, 0, @error, @started,
                    CASE WHEN @status = N'running' THEN NULL ELSE DATEADD(minute, 1, @started) END);
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@feed", feed);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = toDate is { } date ? date.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@error", SqlDbType.NVarChar, 1000) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@started", SqlDbType.DateTimeOffset) { Value = startedAt });
        await command.ExecuteNonQueryAsync();
    }
}
