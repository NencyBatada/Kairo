using Kairo.Contracts.BrandMemory;
using Kairo.Modules.BrandMemory.Data;

namespace Kairo.Modules.BrandMemory.Memory;

internal sealed class BrandMemoryReader(BrandMemoryDbContext db, TimeProvider time) : IBrandMemory
{
    public const int Candidates = 500;
    public const double ProductMatch = 3;
    public const double CategoryMatch = 2;
    public const double RegionMatch = 1;
    public const double Untagged = 0.5;

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

    public async Task<IReadOnlyList<MemoryNote>> UsedForRoomAsync(Guid roomId, string usedIn, CancellationToken cancellationToken)
    {
        var ids = await db.Uses.AsNoTracking()
            .Where(row => row.RoomId == roomId && row.UsedIn == usedIn)
            .Select(row => row.EntryId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var entries = await db.Entries.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        return entries.Select(Note).ToList();
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

    private static HashSet<string> Set(IEnumerable<string>? values) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
