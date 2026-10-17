using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandMemory.Data;

namespace Kairo.Modules.BrandMemory.Memory;

internal sealed class MemoryAskResource(BrandMemoryDbContext db, CurrentMember member, IAuditLog audit, ILiveUpdates live, TimeProvider time) : IAskResource
{
    public string Kind => "brand_memory";
    public BrandRoles Editors => BrandRoles.Admin | BrandRoles.Marketer;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var entry = await EntryAsync(target, ct);
        return entry is null ? null : new(entry.Title, $"brand-kit/memory/{entry.Id}", AskJson.Element(new { entry.Title, entry.Lesson, entry.Kind, entry.Outcome, entry.Evidence, entry.SourceRoomId }), AskJson.Revision(new { entry.Id, entry.Version, entry.State }));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        AskJson.Only(changes, "title", "lesson");
        var entry = await EntryAsync(target, ct) ?? throw new AskResourceException("This memory is unavailable.", 404);
        var patch = AskJson.Read<EditMemoryEntry>(changes) with { Version = entry.Version };
        var title = patch.Title?.Trim() ?? entry.Title;
        var lesson = patch.Lesson?.Trim() ?? entry.Lesson;
        if (title.Length is < 1 or > MemoryEndpoints.TitleLength || lesson.Length is < 1 or > MemoryEndpoints.LessonLength)
            throw new AskResourceException("Use a title of 1 to 120 characters and a lesson of 1 to 500 characters.", 400);
        return new(AskJson.Element(new { entry.Title, entry.Lesson }), AskJson.Element(new { Title = title, Lesson = lesson }),
            AskJson.Element(new EditMemoryEntry(entry.Version, title, lesson)), []);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        var id = target.Id!.Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM brand_memory.MemoryEntries WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND BrandId = {member.BrandId}", ct);
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        AskJson.Result(await MemoryEndpoints.EditAsync(id, AskJson.Read<EditMemoryEntry>(payload), db, member, audit, live, time, ct));
    }
    private Task<MemoryEntry?> EntryAsync(AskTarget target, CancellationToken ct) => db.Entries.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id && row.State == MemoryStates.Active, ct);
}
