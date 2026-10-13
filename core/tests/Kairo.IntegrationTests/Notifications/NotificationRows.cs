using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Notifications;

internal sealed record NotificationRow(long Id, Guid UserId, string Kind, string Title, string Body, string Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal sealed record EmailRow(
    long Id,
    Guid UserId,
    long NotificationId,
    string Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? ClaimedUntil,
    DateTimeOffset? SentAt,
    string? LastError);

internal static class NotificationRows
{
    public static async Task<IReadOnlyList<NotificationRow>> NotificationsAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, UserId, Kind, Title, Body, Link, CreatedAt, ReadAt
            FROM notifications.Notifications
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<NotificationRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new NotificationRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<EmailRow>> EmailsAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, UserId, NotificationId, Status, Attempts, NextAttemptAt, ClaimedUntil, SentAt, LastError
            FROM notifications.OutboundEmails
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<EmailRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new EmailRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    public static async Task<int> ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = connection.CreateCommand();
        context.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;";
        await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
