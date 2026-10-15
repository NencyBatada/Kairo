using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal static class AuditLogEndpoints
{
    public static void MapAuditLog(this IEndpointRouteBuilder api)
    {
        var auditLog = api.MapBrandApi().MapGroup("/audit-log").RequireBrandRole(BrandRoles.Admin);
        auditLog.MapGet("", ListAsync);
        auditLog.MapGet("/export", ExportAsync).Produces<string>(StatusCodes.Status200OK, "text/csv");
        auditLog.MapGet("/actions", ListActions);
    }

    private static async Task<Results<Ok<AuditLogPage>, ValidationProblem>> ListAsync(
        [AsParameters] AuditLogQuery query,
        string? before,
        int? limit,
        AuditDbContext db,
        IBrandDirectory brands,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        long? cursor = null;
        if (!string.IsNullOrWhiteSpace(before))
        {
            if (long.TryParse(before.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                cursor = id;
            }
            else
            {
                errors["before"] = ["Use the nextBefore value from the previous page."];
            }
        }

        var zone = await ZoneAsync(brands, member.BrandId, cancellationToken);
        var filter = AuditLogFilter.TryCreate(query, zone, errors);
        if (filter is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var entries = filter.Apply(db.Entries.AsNoTracking());
        if (cursor is { } last)
        {
            entries = entries.Where(entry => entry.Id < last);
        }

        var found = await entries
            .OrderByDescending(entry => entry.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = found.Take(take).Select(ToResponse).ToList();
        var nextBefore = found.Count > take ? page[^1].Id.ToString(CultureInfo.InvariantCulture) : null;
        return TypedResults.Ok(new AuditLogPage(page, nextBefore));
    }

    private static async Task<Results<PushStreamHttpResult, ValidationProblem>> ExportAsync(
        [AsParameters] AuditLogQuery query,
        AuditDbContext db,
        IBrandDirectory brands,
        CurrentMember member,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var zone = await ZoneAsync(brands, member.BrandId, cancellationToken);
        var filter = AuditLogFilter.TryCreate(query, zone, errors);
        if (filter is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entries = filter.Apply(db.Entries.AsNoTracking())
            .OrderByDescending(entry => entry.Id)
            .AsAsyncEnumerable();
        var today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return TypedResults.Stream(
            stream => AuditCsv.WriteAsync(stream, entries, zone, cancellationToken),
            "text/csv; charset=utf-8",
            $"audit-log-{today}.csv");
    }

    private static Ok<AuditActionOption[]> ListActions() =>
        TypedResults.Ok(AuditActions.All.Select(pair => new AuditActionOption(pair.Key, pair.Value)).ToArray());

    private static async Task<TimeZoneInfo> ZoneAsync(IBrandDirectory brands, Guid brandId, CancellationToken cancellationToken)
    {
        var id = await brands.FindTimeZoneAsync(brandId, cancellationToken);
        return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        AuditActorTypes.Parse(entry.ActorType),
        entry.ActorId,
        entry.ActorName,
        entry.ActorEmail,
        entry.Action,
        entry.TargetType,
        entry.TargetId,
        entry.Before is null ? null : JsonNode.Parse(entry.Before),
        entry.After is null ? null : JsonNode.Parse(entry.After));
}

internal sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    AuditActorType ActorType,
    string? ActorId,
    string ActorName,
    string? ActorEmail,
    string Action,
    string TargetType,
    string TargetId,
    JsonNode? Before,
    JsonNode? After);

internal sealed record AuditLogPage(IReadOnlyList<AuditEntryResponse> Entries, string? NextBefore);

internal sealed record AuditActionOption(string Action, string Label);
