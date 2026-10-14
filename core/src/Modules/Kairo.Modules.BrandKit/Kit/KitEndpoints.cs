using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;

namespace Kairo.Modules.BrandKit.Kit;

internal static class KitEndpoints
{
    public static async Task<Ok<BrandKitView>> GetAsync(KitVersions versions, CancellationToken cancellationToken)
    {
        var approved = await versions.ApprovedAsync(cancellationToken);
        var draft = await versions.DraftAsync(cancellationToken);
        return TypedResults.Ok(new BrandKitView(
            approved is null ? null : await versions.ViewAsync(approved, cancellationToken),
            draft is null ? null : await versions.ViewAsync(draft, cancellationToken)));
    }

    public static async Task<Results<Ok<KitVersionView>, ValidationProblem, ProblemHttpResult>> SaveDraftAsync(
        SaveDraftRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        var check = KitContentRules.Check(request.Content);
        if (!check.IsValid)
        {
            return TypedResults.ValidationProblem(check.Errors);
        }

        await versions.LockAsync(cancellationToken);
        var draft = await versions.DraftAsync(cancellationToken);
        if ((draft?.Revision ?? 0) != request.Revision)
        {
            return Stale();
        }

        var before = draft is null ? null : KitVersions.AuditOf(draft);
        if (draft is null)
        {
            draft = await versions.CreateDraftAsync(check.Content, VersionOrigin.Manual, null, cancellationToken);
        }
        else
        {
            await versions.UpdateDraftAsync(draft, check.Content, cancellationToken);
        }

        await audit.RecordAsync(
            new AuditRecord("brand_kit.draft_saved", KitVersions.AuditTarget, KitVersions.Number(draft), before, KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> DiscardDraftAsync(
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.DraftAsync(cancellationToken) is not { } draft)
        {
            return NoDraft();
        }

        var before = KitVersions.AuditOf(draft);
        await versions.DiscardAsync(draft, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_kit.draft_discarded", KitVersions.AuditTarget, KitVersions.Number(draft), before),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<KitVersionView>, ProblemHttpResult>> ApproveAsync(
        RevisionRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.DraftAsync(cancellationToken) is not { } draft)
        {
            return NoDraft();
        }

        if (draft.Revision != request.Revision)
        {
            return Stale();
        }

        await versions.ApproveAsync(draft, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_kit.approved", KitVersions.AuditTarget, KitVersions.Number(draft), After: KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }

    public static async Task<Ok<KitVersionSummary[]>> HistoryAsync(KitVersions versions, CancellationToken cancellationToken) =>
        TypedResults.Ok((await versions.HistoryAsync(cancellationToken)).Select(KitVersionSummary.Of).ToArray());

    public static async Task<Results<Ok<KitVersionView>, NotFound>> GetVersionAsync(
        int number,
        KitVersions versions,
        CancellationToken cancellationToken)
    {
        if (await versions.FindAsync(number, cancellationToken) is not { } version)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await versions.ViewAsync(version, cancellationToken));
    }

    public static async Task<Results<Ok<KitVersionView>, NotFound, ProblemHttpResult>> RestoreAsync(
        int number,
        RevisionRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.FindAsync(number, cancellationToken) is not { } source)
        {
            return TypedResults.NotFound();
        }

        if (source.Status != VersionStatus.Superseded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: KitVersions.NotRestorable);
        }

        var draft = await versions.DraftAsync(cancellationToken);
        if ((draft?.Revision ?? 0) != request.Revision)
        {
            return Stale();
        }

        var before = draft is null ? null : KitVersions.AuditOf(draft);
        var content = KitJson.Read(source.Content);
        if (draft is null)
        {
            draft = await versions.CreateDraftAsync(content, VersionOrigin.Restored, number, cancellationToken);
        }
        else
        {
            await versions.RestoreIntoAsync(draft, content, number, cancellationToken);
        }

        await audit.RecordAsync(
            new AuditRecord("brand_kit.restored", KitVersions.AuditTarget, KitVersions.Number(draft), before, KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }

    public static ProblemHttpResult Stale() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: KitVersions.StaleDraft);

    public static ProblemHttpResult NoDraft() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: KitVersions.NoDraft);
}
