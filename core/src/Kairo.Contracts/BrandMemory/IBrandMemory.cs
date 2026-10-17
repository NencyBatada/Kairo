namespace Kairo.Contracts.BrandMemory;

public interface IBrandMemory
{
    Task<IReadOnlyList<MemoryNote>> RelevantAsync(MemoryQuery query, CancellationToken cancellationToken);

    Task RecordUsesAsync(IReadOnlyCollection<Guid> entryIds, string usedIn, Guid targetId, Guid? roomId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> UsedByAsync(string usedIn, Guid targetId, CancellationToken cancellationToken);

    Task<Guid> SaveAsync(MemoryDraft draft, CancellationToken cancellationToken);
}

public sealed record MemoryDraft(
    string Kind,
    string Outcome,
    string Title,
    string Lesson,
    string Evidence,
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Regions,
    string CreatedBy);

public sealed record MemoryQuery(
    IReadOnlyCollection<string>? Products = null,
    IReadOnlyCollection<string>? Categories = null,
    IReadOnlyCollection<string>? Regions = null,
    IReadOnlyCollection<string>? Kinds = null,
    int Limit = MemoryQuery.DefaultLimit)
{
    public const int DefaultLimit = 10;
    public const int MostEntries = 50;
}

public sealed record MemoryNote(
    Guid Id,
    string Kind,
    string Outcome,
    string Title,
    string Lesson,
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Regions,
    bool Removed = false)
{
    public string Text => $"{(Outcome == MemoryOutcomes.Worked ? "Worked" : "Didn't work")} ({Kind}): {Lesson}";
}

public static class MemoryKinds
{
    public const string Message = "message";
    public const string Visual = "visual";
    public const string Audience = "audience";
    public const string Region = "region";
    public const string Change = "change";

    public static readonly string[] All = [Message, Visual, Audience, Region, Change];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

public static class MemoryOutcomes
{
    public const string Worked = "worked";
    public const string DidntWork = "didnt_work";

    public static readonly string[] All = [Worked, DidntWork];

    public static bool IsKnown(string? outcome) => outcome is not null && All.Contains(outcome);
}

public static class MemoryUsedIn
{
    public const string Brief = "brief";
    public const string Asset = "asset";
    public const string Opportunity = "opportunity";
}
