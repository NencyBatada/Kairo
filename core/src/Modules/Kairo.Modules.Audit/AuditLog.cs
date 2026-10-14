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
        var actor = record.Actor ?? CurrentActor();
        db.Entries.Add(new AuditEntry
        {
            OccurredAt = time.GetUtcNow(),
            ActorType = AuditActorTypes.KeyOf(actor.Type),
            ActorId = actor.Id,
            ActorName = actor.Name,
            ActorEmail = actor.Email,
            Action = record.Action,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            Before = Serialize(record.Before),
            After = Serialize(record.After),
            TraceId = Activity.Current?.TraceId.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditActor CurrentActor()
    {
        var principal = http.HttpContext?.User;
        return principal?.FindUserId() is { } userId
            ? new AuditActor(
                AuditActorType.User,
                principal.FindFirstValue(KairoClaims.Name) ?? "",
                userId.ToString(),
                principal.FindFirstValue(KairoClaims.Email))
            : new AuditActor(AuditActorType.System, "Kairo");
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
