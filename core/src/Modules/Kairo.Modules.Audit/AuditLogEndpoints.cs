using System.Text.Json.Nodes;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal static class AuditLogEndpoints
{
    public static void MapAuditLog(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/audit-log", ListAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Ok<AuditLogPage>> ListAsync(
        long? before,
        int? limit,
        AuditDbContext db,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Entries.AsNoTracking();
        if (before is { } cursor)
        {
            query = query.Where(entry => entry.Id < cursor);
        }

        var entries = await query
            .OrderByDescending(entry => entry.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = entries.Take(take).Select(ToResponse).ToList();
        var nextBefore = entries.Count > take ? page[^1].Id.ToString() : null;
        return TypedResults.Ok(new AuditLogPage(page, nextBefore));
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        entry.ActorType,
        entry.ActorId,
        entry.ActorName,
        entry.Action,
        entry.TargetType,
        entry.TargetId,
        entry.Before is null ? null : JsonNode.Parse(entry.Before),
        entry.After is null ? null : JsonNode.Parse(entry.After));
}

internal sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string ActorType,
    string? ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    JsonNode? Before,
    JsonNode? After);

internal sealed record AuditLogPage(IReadOnlyList<AuditEntryResponse> Entries, string? NextBefore);
