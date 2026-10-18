using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Microsoft.Extensions.Hosting;

public sealed class CloudLogFormatter() : ConsoleFormatter("kairo")
{
    private static readonly HashSet<string> IdFields = ["BrandId", "CampaignId", "RoomId", "RunId", "LaunchId", "BatchId"];
    private static readonly HashSet<string> NumberFields = ["Attempt", "Status", "StatusCode", "Count"];
    private static readonly HashSet<string> Events =
    [
        "launch.failed", "launch.succeeded", "stage.failed_twice", "mass_pause.held",
        "feed.stale", "connection.expired", "integration.failed", "integration.quota_exceeded",
    ];

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopeProvider, TextWriter writer)
    {
        var fields = new Dictionary<string, object?>
        {
            ["severity"] = entry.LogLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARNING",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRITICAL",
                _ => "DEFAULT",
            },
            ["category"] = entry.Category,
            ["eventId"] = entry.EventId.Id,
        };
        if (entry.State is IEnumerable<KeyValuePair<string, object?>> state)
        {
            foreach (var pair in state)
            {
                if (pair.Key == "{OriginalFormat}" && pair.Value is string template) fields["message"] = template;
                else if (IdFields.Contains(pair.Key) && Guid.TryParse(pair.Value?.ToString(), out var id)) fields[pair.Key] = id;
                else if (NumberFields.Contains(pair.Key) && pair.Value is int or long) fields[pair.Key] = pair.Value;
                else if (pair.Key == "Event" && pair.Value is string name && Events.Contains(name)) fields["event"] = name;
            }
        }
        fields.TryAdd("message", "Application log");
        if (entry.Exception is { } exception) fields["exceptionType"] = exception.GetType().FullName;
        if (Activity.Current is { } activity)
        {
            fields["traceId"] = activity.TraceId.ToString();
            fields["spanId"] = activity.SpanId.ToString();
            foreach (var name in new[] { "brand", "campaign", "run" })
                if (Guid.TryParse(activity.GetTagItem($"kairo.{name}_id")?.ToString(), out var id)) fields[name + "Id"] = id;
            if (Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") is { Length: > 0 } project)
                fields["logging.googleapis.com/trace"] = $"projects/{project}/traces/{activity.TraceId}";
            fields["logging.googleapis.com/spanId"] = activity.SpanId.ToString();
        }
        writer.WriteLine(JsonSerializer.Serialize(fields));
    }
}
