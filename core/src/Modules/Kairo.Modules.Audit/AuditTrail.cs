using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal sealed class AuditTrail(AuditDbContext db) : IAuditTrail
{
    public const int MaxLimit = 200;

    public async Task<AuditTrailPage> ForTargetAsync(string targetType, string targetId, long? before, int limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, MaxLimit);
        var entries = db.Entries.AsNoTracking().Where(entry => entry.TargetType == targetType && entry.TargetId == targetId);
        if (before is { } last)
        {
            entries = entries.Where(entry => entry.Id < last);
        }

        var found = await entries.OrderByDescending(entry => entry.Id).Take(take + 1).ToListAsync(cancellationToken);
        var page = found.Take(take).Select(ToEntry).ToList();
        return new AuditTrailPage(page, found.Count > take ? page[^1].Id : null);
    }

    private static AuditTrailEntry ToEntry(AuditEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        AuditActorTypes.Parse(entry.ActorType),
        entry.ActorId,
        entry.ActorName,
        entry.Action,
        Parse(entry.Before),
        Parse(entry.After));

    private static JsonElement? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
