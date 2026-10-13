using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal sealed class AuditLog(AuditDbContext db, IHttpContextAccessor http, TimeProvider time) : IAuditLog
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var principal = http.HttpContext?.User;
        var userId = principal?.FindUserId();
        db.Entries.Add(new AuditEntry
        {
            OccurredAt = time.GetUtcNow(),
            ActorType = userId is null ? "system" : "user",
            ActorId = userId?.ToString(),
            ActorName = userId is null ? "Kairo" : principal!.FindFirstValue(KairoClaims.Name) ?? "",
            Action = record.Action,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            Before = Serialize(record.Before),
            After = Serialize(record.After),
            TraceId = Activity.Current?.TraceId.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
