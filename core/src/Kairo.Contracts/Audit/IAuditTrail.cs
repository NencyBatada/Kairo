using System.Text.Json;

namespace Kairo.Contracts.Audit;

public interface IAuditTrail
{
    Task<AuditTrailPage> ForTargetAsync(string targetType, string targetId, long? before, int limit, CancellationToken cancellationToken);
}

public sealed record AuditTrailEntry(
    long Id,
    DateTimeOffset OccurredAt,
    AuditActorType ActorType,
    string? ActorId,
    string ActorName,
    string Action,
    JsonElement? Before,
    JsonElement? After);

public sealed record AuditTrailPage(IReadOnlyList<AuditTrailEntry> Entries, long? NextBefore);
