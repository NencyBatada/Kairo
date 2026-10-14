using System.Globalization;
using System.Text;
using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal static class AuditCsv
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task WriteAsync(Stream stream, IAsyncEnumerable<AuditEntry> entries, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, Utf8WithBom, bufferSize: 16 * 1024, leaveOpen: true);
        await writer.WriteAsync(Row(
            "Occurred at (UTC)",
            $"Occurred at ({zone.Id})",
            "Actor type",
            "Actor",
            "Actor email",
            "Action",
            "Action key",
            "Target type",
            "Target ID",
            "Before",
            "After"));
        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            await writer.WriteAsync(Row(
                entry.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(entry.OccurredAt, zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                entry.ActorType,
                entry.ActorName,
                entry.ActorEmail,
                AuditActions.LabelOf(entry.Action),
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.Before,
                entry.After));
        }

        await writer.FlushAsync(cancellationToken);
    }

    public static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string Row(params string?[] values) => string.Join(',', values.Select(Cell)) + "\r\n";
}
