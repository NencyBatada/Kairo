using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Drafting;

internal static class DraftingEndpoints
{
    public const string AlreadyDrafting = "Kairo is already drafting the Brand Kit.";
    public const string DraftExists = "There's already a draft. Replace it to draft again.";

    public static async Task<Results<Accepted<DraftRunView>, ValidationProblem, ProblemHttpResult>> StartAsync(
        DraftingRequest request,
        BrandKitDbContext db,
        KitVersions versions,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var websiteUrl = (request.WebsiteUrl ?? "").Trim();
        if (!KitContentRules.IsWebAddress(websiteUrl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["websiteUrl"] = [KitContentRules.NotWebAddress] });
        }

        await versions.LockAsync(cancellationToken);
        if (await db.DraftRuns.AnyAsync(run => run.Status == DraftRunStatus.Pending || run.Status == DraftRunStatus.Running, cancellationToken))
        {
            return Conflict(AlreadyDrafting);
        }

        if (!request.ReplaceDraft && await versions.DraftAsync(cancellationToken) is not null)
        {
            return Conflict(DraftExists);
        }

        var run = new DraftRun
        {
            Id = Guid.NewGuid(),
            WebsiteUrl = websiteUrl,
            Status = DraftRunStatus.Pending,
            RequestedBy = member.UserId,
            RequestedAt = time.GetUtcNow(),
        };
        db.DraftRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord("brand_kit.drafting_started", DraftRunView.AuditTarget, run.Id.ToString(), After: new { websiteUrl, replaceDraft = request.ReplaceDraft }),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Drafting(run.Id, run.Status), cancellationToken);
        return TypedResults.Accepted($"/api/brands/{member.BrandId}/brand-kit/drafting/latest", DraftRunView.Of(run));
    }

    public static async Task<Results<Ok<DraftRunView>, NoContent>> LatestAsync(BrandKitDbContext db, CancellationToken cancellationToken)
    {
        var run = await db.DraftRuns
            .AsNoTracking()
            .OrderByDescending(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return run is null ? TypedResults.NoContent() : TypedResults.Ok(DraftRunView.Of(run));
    }

    private static ProblemHttpResult Conflict(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
