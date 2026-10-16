using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandMemory.Data;
using Kairo.Modules.BrandMemory.Memory;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandMemory.Learning;

internal sealed record LearnInput(Guid RoomId, Guid? ReportId, LearnRequest Request);

internal static class ReportOutcomes
{
    public const string Worked = "worked";
    public const string NoEffect = "no_effect";
    public const string Inconclusive = "inconclusive";

    public static bool Proven(string? outcome) => outcome is Worked or NoEffect;
}

internal sealed class LearnWork(
    BrandMemoryDbContext db,
    KairoDatabase database,
    LearnAgent agent,
    ICampaignLifecycle lifecycle,
    IAuditLog audit,
    ILiveUpdates live,
    TimeProvider time,
    ILogger<LearnWork> logger)
{
    public const int MostLessons = 8;
    public const int MostAttempts = 3;
    public const int MaxErrorLength = 1000;
    public const long MinClicks = 100;
    public const double MinConversions = 15;
    public const string Creator = "learn";

    public static readonly AuditActor Actor = AuditActor.Agent("Learn agent", "learn");

    public static string LockName(Guid roomId) => $"brand-memory-learn:{roomId:N}";

    public async Task<LearnRun> LearnAsync(LearnInput input, CancellationToken cancellationToken)
    {
        if (await RunAsync(input.RoomId, cancellationToken) is { Outcome: not LearnOutcomes.Failed } done)
        {
            return done;
        }

        if (!ReportOutcomes.Proven(input.Request.Result.Outcome))
        {
            return await SaveAsync(input, LearnOutcomes.SkippedInconclusive, [], null, cancellationToken);
        }

        LearnAnswer answer;
        try
        {
            answer = await agent.LearnAsync(input.Request, cancellationToken);
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "The Learn agent failed for campaign {RoomId}.", input.RoomId);
            return await FailAsync(input, exception.Message, cancellationToken);
        }

        return await SaveAsync(input, LearnOutcomes.Saved, Entries(input, answer.Lessons), answer, cancellationToken);
    }

    public static List<MemoryEntry> Entries(LearnInput input, IEnumerable<LearnedLesson?>? lessons)
    {
        var request = input.Request;
        var products = request.Campaign.Products.Select(product => product.Id).Distinct(StringComparer.Ordinal).ToList();
        var categories = request.Campaign.Products.Select(product => product.Category?.Trim()).OfType<string>().Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var testRegions = request.Regions.Where(region => region.Test).Select(region => region.Name).ToList();
        var known = request.Regions.Select(region => region.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = request.Result.Outcome == ReportOutcomes.Worked ? MemoryOutcomes.Worked : MemoryOutcomes.DidntWork;
        var ads = request.Ads.GroupBy(ad => ad.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var entries = new List<MemoryEntry>();
        foreach (var lesson in (lessons ?? []).OfType<LearnedLesson>())
        {
            var title = Cut(lesson.Title, MemoryEndpoints.TitleLength);
            var text = Cut(lesson.Lesson, MemoryEndpoints.LessonLength);
            if (!MemoryKinds.IsKnown(lesson.Kind) || !MemoryOutcomes.IsKnown(lesson.Outcome) || title is null || text is null
                || entries.Any(entry => string.Equals(entry.Title, title, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (lesson.Kind is MemoryKinds.Audience or MemoryKinds.Region && lesson.Outcome != expected)
            {
                continue;
            }

            if (lesson.Kind is MemoryKinds.Message or MemoryKinds.Visual)
            {
                var cited = (lesson.AdIds ?? []).Distinct(StringComparer.Ordinal).ToList();
                if (cited.Count == 0 || !cited.All(id => ads.TryGetValue(id, out var ad) && Enough(ad)))
                {
                    continue;
                }
            }

            var regions = (lesson.Regions ?? []).Where(known.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            entries.Add(new MemoryEntry
            {
                Id = Guid.NewGuid(),
                Kind = lesson.Kind!,
                Outcome = lesson.Outcome!,
                Title = title,
                Lesson = text,
                Evidence = lesson.Evidence is { ValueKind: JsonValueKind.Object } evidence ? evidence.GetRawText() : "{}",
                Products = products,
                Categories = categories,
                Regions = regions.Count > 0 ? regions : testRegions,
                Languages = [.. (lesson.Languages ?? []).Where(language => language is { Length: >= 2 and <= 8 }).Distinct(StringComparer.OrdinalIgnoreCase)],
                SourceRoomId = input.RoomId,
                SourceReportId = input.ReportId,
                State = MemoryStates.Active,
                CreatedBy = Creator,
            });
            if (entries.Count == MostLessons)
            {
                break;
            }
        }

        return entries;
    }

    public static bool Enough(LearnAd ad) => ad.Clicks >= MinClicks || ad.Conversions >= MinConversions;

    private async Task<LearnRun?> RunAsync(Guid roomId, CancellationToken cancellationToken) =>
        await db.LearnRuns.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == roomId, cancellationToken);

    private async Task<LearnRun> SaveAsync(LearnInput input, string outcome, List<MemoryEntry> entries, LearnAnswer? answer, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(input.RoomId), cancellationToken);
            var run = await db.LearnRuns.SingleOrDefaultAsync(row => row.RoomId == input.RoomId, cancellationToken);
            if (run is { Outcome: not LearnOutcomes.Failed })
            {
                await database.RollbackAsync(cancellationToken);
                return run;
            }

            var now = time.GetUtcNow();
            run ??= db.LearnRuns.Add(new LearnRun { Id = Guid.NewGuid(), RoomId = input.RoomId }).Entity;
            run.ReportId = input.ReportId;
            run.Outcome = outcome;
            run.Entries = entries.Count;
            run.Attempts++;
            run.Error = null;
            run.Model = answer?.Model;
            run.PromptVersion = answer?.PromptVersion;
            run.UpdatedAt = now;
            foreach (var entry in entries)
            {
                entry.CreatedAt = now;
            }

            db.Entries.AddRange(entries);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var entry in entries)
            {
                await audit.RecordAsync(
                    new AuditRecord("brand_memory.entry_saved", MemoryEndpoints.AuditTarget, entry.Id.ToString(),
                        After: new { entry.Kind, entry.Outcome, entry.Title, campaignId = input.RoomId, reportId = input.ReportId }, Actor: Actor),
                    cancellationToken);
            }

            if (entries.Count > 0)
            {
                await live.PublishAsync(MemoryEndpoints.Changed(), cancellationToken);
            }

            await lifecycle.AdvanceAsync(input.RoomId, CampaignSignals.LearningsSaved, Actor, cancellationToken);
            await database.CommitAsync(cancellationToken);
            return run;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<LearnRun> FailAsync(LearnInput input, string error, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(input.RoomId), cancellationToken);
            var run = await db.LearnRuns.SingleOrDefaultAsync(row => row.RoomId == input.RoomId, cancellationToken);
            if (run is { Outcome: not LearnOutcomes.Failed })
            {
                await database.RollbackAsync(cancellationToken);
                return run;
            }

            run ??= db.LearnRuns.Add(new LearnRun { Id = Guid.NewGuid(), RoomId = input.RoomId }).Entity;
            run.ReportId = input.ReportId;
            run.Outcome = LearnOutcomes.Failed;
            run.Attempts++;
            run.Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
            run.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await database.CommitAsync(cancellationToken);
            return run;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static string? Cut(string? text, int length)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > length ? trimmed[..length].TrimEnd() : trimmed;
    }
}
