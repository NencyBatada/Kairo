using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandMemory.Data;

namespace Kairo.Modules.BrandMemory.Memory;

internal sealed record MemoryEntryView(
    Guid Id,
    int Version,
    string Kind,
    string Outcome,
    string Title,
    string Lesson,
    JsonElement Evidence,
    string[] Products,
    string[] Categories,
    string[] Regions,
    string[] Languages,
    Guid? SourceCampaignId,
    string State,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? EditedBy,
    DateTimeOffset? EditedAt,
    Guid? RemovedBy,
    DateTimeOffset? RemovedAt,
    int Uses);

internal sealed record MemoryUseView(string UsedIn, Guid TargetId, Guid? CampaignId, DateTimeOffset UsedAt);

internal sealed record MemoryEntryList(MemoryEntryView[] Entries);

internal sealed record MemoryEntryDetail(MemoryEntryView Entry, MemoryUseView[] Uses);

internal sealed record EditMemoryEntry(int? Version, string? Title, string? Lesson);

internal static class MemoryEndpoints
{
    public const int ListLimit = 200;
    public const int UseLimit = 100;
    public const int TitleLength = 120;
    public const int LessonLength = 500;
    public const string AuditTarget = "memory_entry";

    public static LiveEvent Changed(Guid? entryId = null) => new("brand_memory.changed", new { entryId }, LiveAudience.Everyone);

    public static async Task<Results<Ok<MemoryEntryList>, ValidationProblem>> ListAsync(
        string? kind, string? outcome, string? state, BrandMemoryDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (kind is not null && !MemoryKinds.IsKnown(kind)) errors["kind"] = [$"Use one of: {string.Join(", ", MemoryKinds.All)}."];
        if (outcome is not null && !MemoryOutcomes.IsKnown(outcome)) errors["outcome"] = [$"Use one of: {string.Join(", ", MemoryOutcomes.All)}."];
        if (state is not null && !MemoryStates.IsKnown(state)) errors["state"] = [$"Use {MemoryStates.Active} or {MemoryStates.Removed}."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        var wanted = state ?? MemoryStates.Active;
        var entries = await db.Entries.AsNoTracking()
            .Where(row => row.State == wanted)
            .Where(row => kind == null || row.Kind == kind)
            .Where(row => outcome == null || row.Outcome == outcome)
            .OrderByDescending(row => row.CreatedAt)
            .Take(ListLimit)
            .ToListAsync(cancellationToken);
        var counts = await UseCountsAsync(db, [.. entries.Select(entry => entry.Id)], cancellationToken);
        return TypedResults.Ok(new MemoryEntryList([.. entries.Select(entry => View(entry, counts.GetValueOrDefault(entry.Id)))]));
    }

    public static async Task<Results<Ok<MemoryEntryDetail>, NotFound>> GetAsync(Guid entryId, BrandMemoryDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Entries.AsNoTracking().SingleOrDefaultAsync(row => row.Id == entryId, cancellationToken) is not { } entry)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await DetailAsync(db, entry, cancellationToken));
    }

    public static async Task<Results<Ok<MemoryEntryDetail>, NotFound, ProblemHttpResult, ValidationProblem>> EditAsync(
        Guid entryId,
        EditMemoryEntry request,
        BrandMemoryDbContext db,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not { } version || version < 1) errors["version"] = ["Send the entry's version from when you opened it."];
        var title = request.Title?.Trim();
        var lesson = request.Lesson?.Trim();
        if (title is null && lesson is null) errors["title"] = ["Change the title or the lesson."];
        if (title is { Length: 0 } || title?.Length > TitleLength) errors["title"] = [$"Write a title of 1 to {TitleLength} characters."];
        if (lesson is { Length: 0 } || lesson?.Length > LessonLength) errors["lesson"] = [$"Write a lesson of 1 to {LessonLength} characters."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        if (await db.Entries.SingleOrDefaultAsync(row => row.Id == entryId, cancellationToken) is not { } entry)
        {
            return TypedResults.NotFound();
        }

        if (entry.State != MemoryStates.Active)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This entry was removed, so it can't be edited.");
        }

        if (entry.Version != request.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This entry changed since you opened it. Refresh and try again.");
        }

        var before = new { entry.Title, entry.Lesson };
        entry.Title = title ?? entry.Title;
        entry.Lesson = lesson ?? entry.Lesson;
        entry.EditedBy = member.UserId;
        entry.EditedAt = time.GetUtcNow();
        entry.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This entry changed since you opened it. Refresh and try again.");
        }

        await audit.RecordAsync(new AuditRecord("brand_memory.entry_edited", AuditTarget, entry.Id.ToString(), before, new { entry.Title, entry.Lesson }), cancellationToken);
        await live.PublishAsync(Changed(entry.Id), cancellationToken);
        return TypedResults.Ok(await DetailAsync(db, entry, cancellationToken));
    }

    public static async Task<Results<NoContent, NotFound>> RemoveAsync(
        Guid entryId,
        BrandMemoryDbContext db,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.Entries.SingleOrDefaultAsync(row => row.Id == entryId, cancellationToken) is not { } entry)
        {
            return TypedResults.NotFound();
        }

        if (entry.State == MemoryStates.Removed)
        {
            return TypedResults.NoContent();
        }

        entry.State = MemoryStates.Removed;
        entry.RemovedBy = member.UserId;
        entry.RemovedAt = time.GetUtcNow();
        entry.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_memory.entry_removed", AuditTarget, entry.Id.ToString(), new { State = MemoryStates.Active, entry.Title }, new { entry.State, entry.Title }),
            cancellationToken);
        await live.PublishAsync(Changed(entry.Id), cancellationToken);
        return TypedResults.NoContent();
    }

    public static MemoryEntryView View(MemoryEntry entry, int uses) => new(
        entry.Id,
        entry.Version,
        entry.Kind,
        entry.Outcome,
        entry.Title,
        entry.Lesson,
        JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(entry.Evidence) ? "{}" : entry.Evidence),
        [.. entry.Products],
        [.. entry.Categories],
        [.. entry.Regions],
        [.. entry.Languages],
        entry.SourceRoomId,
        entry.State,
        entry.CreatedBy,
        entry.CreatedAt,
        entry.EditedBy,
        entry.EditedAt,
        entry.RemovedBy,
        entry.RemovedAt,
        uses);

    private static async Task<MemoryEntryDetail> DetailAsync(BrandMemoryDbContext db, MemoryEntry entry, CancellationToken cancellationToken)
    {
        var uses = await db.Uses.AsNoTracking()
            .Where(row => row.EntryId == entry.Id)
            .OrderByDescending(row => row.UsedAt)
            .Take(UseLimit)
            .ToListAsync(cancellationToken);
        var count = await db.Uses.CountAsync(row => row.EntryId == entry.Id, cancellationToken);
        return new MemoryEntryDetail(View(entry, count), [.. uses.Select(use => new MemoryUseView(use.UsedIn, use.TargetId, use.RoomId, use.UsedAt))]);
    }

    private static async Task<Dictionary<Guid, int>> UseCountsAsync(BrandMemoryDbContext db, List<Guid> ids, CancellationToken cancellationToken) =>
        await db.Uses.AsNoTracking()
            .Where(row => ids.Contains(row.EntryId))
            .GroupBy(row => row.EntryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
}
