using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandMemory.Data;

namespace Kairo.Modules.BrandMemory.Memory;

internal sealed class BrandMemoryReader(BrandMemoryDbContext db, IAuditLog audit, ILiveUpdates live, TimeProvider time) : IBrandMemory
{
    public const int Candidates = 500;
    public const double ProductMatch = 3;
    public const double CategoryMatch = 2;
    public const double RegionMatch = 1;
    public const double Untagged = 0.5;
    public const int MostTags = 50;
    public const int TagLength = 200;

    public async Task<IReadOnlyList<MemoryNote>> RelevantAsync(MemoryQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, MemoryQuery.MostEntries);
        var kinds = (query.Kinds ?? []).Where(MemoryKinds.IsKnown).ToList();
        var entries = await db.Entries.AsNoTracking()
            .Where(row => row.State == MemoryStates.Active)
            .Where(row => kinds.Count == 0 || kinds.Contains(row.Kind))
            .OrderByDescending(row => row.CreatedAt)
            .Take(Candidates)
            .ToListAsync(cancellationToken);
        return Rank(entries, query).Take(limit).Select(Note).ToList();
    }

    public static IEnumerable<MemoryEntry> Rank(IEnumerable<MemoryEntry> entries, MemoryQuery query)
    {
        var products = Set(query.Products);
        var categories = Set(query.Categories);
        var regions = Set(query.Regions);
        var filtered = products.Count + categories.Count + regions.Count > 0;
        return entries
            .Select(entry => (Entry: entry, Score: Score(entry, products, categories, regions)))
            .Where(scored => !filtered || scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .ThenByDescending(scored => scored.Entry.CreatedAt)
            .Select(scored => scored.Entry);
    }

    public async Task RecordUsesAsync(IReadOnlyCollection<Guid> entryIds, string usedIn, Guid targetId, Guid? roomId, CancellationToken cancellationToken)
    {
        var wanted = entryIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var active = await db.Entries.AsNoTracking()
            .Where(row => wanted.Contains(row.Id) && row.State == MemoryStates.Active)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var recorded = await db.Uses.AsNoTracking()
            .Where(row => row.UsedIn == usedIn && row.TargetId == targetId && active.Contains(row.EntryId))
            .Select(row => row.EntryId)
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var added = active.Except(recorded).Select(id => new MemoryUse
        {
            Id = Guid.NewGuid(),
            EntryId = id,
            UsedIn = usedIn,
            TargetId = targetId,
            RoomId = roomId,
            UsedAt = now,
        }).ToList();
        if (added.Count == 0)
        {
            return;
        }

        db.Uses.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryNote>> UsedByAsync(string usedIn, Guid targetId, CancellationToken cancellationToken)
    {
        var ids = await db.Uses.AsNoTracking()
            .Where(row => row.TargetId == targetId && row.UsedIn == usedIn)
            .Select(row => row.EntryId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var entries = await db.Entries.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        return entries.Select(Note).ToList();
    }

    public async Task<Guid> SaveAsync(MemoryDraft draft, CancellationToken cancellationToken)
    {
        if (!MemoryKinds.IsKnown(draft.Kind) || !MemoryOutcomes.IsKnown(draft.Outcome)
            || Cut(draft.Title, MemoryEndpoints.TitleLength) is not { } title || Cut(draft.Lesson, MemoryEndpoints.LessonLength) is not { } lesson)
        {
            throw new ArgumentException("A Brand Memory entry needs a known kind and outcome, a title and a lesson.", nameof(draft));
        }

        var entry = new MemoryEntry
        {
            Id = Guid.NewGuid(),
            Kind = draft.Kind,
            Outcome = draft.Outcome,
            Title = title,
            Lesson = lesson,
            Evidence = string.IsNullOrWhiteSpace(draft.Evidence) ? "{}" : draft.Evidence,
            Products = Tags(draft.Products),
            Categories = Tags(draft.Categories),
            Regions = Tags(draft.Regions),
            State = MemoryStates.Active,
            CreatedBy = Cut(draft.CreatedBy, 40) ?? "",
            CreatedAt = time.GetUtcNow(),
        };
        db.Entries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_memory.entry_saved", MemoryEndpoints.AuditTarget, entry.Id.ToString(),
                After: new { entry.Kind, entry.Outcome, entry.Title, source = entry.CreatedBy }),
            cancellationToken);
        await live.PublishAsync(MemoryEndpoints.Changed(entry.Id), cancellationToken);
        return entry.Id;
    }

    public static MemoryNote Note(MemoryEntry entry) => new(
        entry.Id,
        entry.Kind,
        entry.Outcome,
        entry.Title,
        entry.Lesson,
        [.. entry.Products],
        [.. entry.Categories],
        [.. entry.Regions],
        entry.State == MemoryStates.Removed);

    private static double Score(MemoryEntry entry, HashSet<string> products, HashSet<string> categories, HashSet<string> regions)
    {
        if (entry.Products.Count + entry.Categories.Count + entry.Regions.Count == 0)
        {
            return Untagged;
        }

        return (ProductMatch * entry.Products.Count(products.Contains))
            + (CategoryMatch * entry.Categories.Count(categories.Contains))
            + (RegionMatch * entry.Regions.Count(regions.Contains));
    }

    private static List<string> Tags(IEnumerable<string> values) =>
        [.. values.Select(value => Cut(value, TagLength)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(MostTags)];

    private static string? Cut(string? text, int length)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length == 0 ? null : trimmed.Length > length ? trimmed[..length] : trimmed;
    }

    private static HashSet<string> Set(IEnumerable<string>? values) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
