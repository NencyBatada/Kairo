using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Files;
using Kairo.Modules.BrandKit.Kit;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed class DraftRunner(
    BrandKitDbContext db,
    KairoDatabase database,
    KitVersions versions,
    WebsiteReader website,
    BestAds bestAds,
    DraftAgent agent,
    LogoSaver logos,
    IBrandDirectory brands,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    TimeProvider time,
    ILogger<DraftRunner> logger)
{
    public const int MaxAttempts = 2;
    public const string SiteUnreadable = "site_unreadable";
    public const string DraftingFailed = "drafting_failed";
    public const string FailedMessage = "Kairo couldn't draft the Brand Kit this time. Try again, or fill it in by hand.";
    public const string ReadyTitle = "Your Brand Kit draft is ready";
    public const string FailedTitle = "Kairo couldn't draft the Brand Kit";
    public const string NotificationKind = "brand_kit.drafted";

    public static string UnreadableMessage(string website) =>
        $"Kairo couldn't read {website}. You can fill in the Brand Kit by hand.";

    public async Task RunAsync(Guid runId, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var run = await db.DraftRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == runId && row.ClaimedUntil == lease, cancellationToken);
        if (run is null)
        {
            return;
        }

        var brand = (await brands.GetAsync([run.BrandId], cancellationToken)).Single();
        if (run.Attempts > MaxAttempts)
        {
            await FailAsync(run.Id, lease, brand, DraftingFailed, FailedMessage, null, [], cancellationToken);
            return;
        }

        await live.PublishAsync(BrandKitLiveEvents.Drafting(run.Id, DraftRunStatus.Running), cancellationToken);
        var reading = await website.ReadAsync(new Uri(run.WebsiteUrl), cancellationToken);
        var ads = await bestAds.ReadAsync(cancellationToken);
        if (reading.Pages.Count == 0 && ads.Count == 0)
        {
            await FailAsync(run.Id, lease, brand, SiteUnreadable, UnreadableMessage(run.WebsiteUrl), reading, ads, cancellationToken);
            return;
        }

        DraftAnswer answer;
        DraftOutcome outcome;
        try
        {
            answer = await agent.DraftAsync(DraftAnswerMapping.Request(brand, reading, ads), cancellationToken);
            outcome = DraftAnswerMapping.Map(answer, reading, ads)
                ?? throw new AgentCallException(AgentCallException.BadAnswer, "The agent's answer doesn't match the Brand Kit.");
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "Drafting run {RunId} failed on attempt {Attempt}: {Code}.", run.Id, run.Attempts, exception.Code);
            if (run.Attempts < MaxAttempts)
            {
                await ReleaseAsync(run.Id, lease, cancellationToken);
                return;
            }

            await FailAsync(run.Id, lease, brand, DraftingFailed, FailedMessage, reading, ads, cancellationToken);
            return;
        }

        var logo = outcome.Logo is { } chosen ? await logos.DownloadAsync(chosen, run.BrandId, run.RequestedBy, cancellationToken) : null;
        await SucceedAsync(run.Id, lease, brand, outcome, logo, answer, reading, ads, cancellationToken);
    }

    private async Task SucceedAsync(
        Guid runId,
        DateTimeOffset lease,
        BrandSummary brand,
        DraftOutcome outcome,
        KitFile? logo,
        DraftAnswer answer,
        WebsiteReading reading,
        IReadOnlyList<BestAd> ads,
        CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await versions.LockAsync(cancellationToken);
            var run = await db.DraftRuns.SingleOrDefaultAsync(row => row.Id == runId && row.ClaimedUntil == lease, cancellationToken);
            if (run is null)
            {
                await database.RollbackAsync(cancellationToken);
                return;
            }

            versions.ActFor(run.RequestedBy);
            var draft = await versions.DraftAsync(cancellationToken);
            var basis = draft ?? await versions.ApprovedAsync(cancellationToken);
            var kept = basis is null ? new KitContent() : KitJson.Read(basis.Content);
            var content = outcome.Content with { Rules = kept.Rules, GuidelineFileIds = kept.GuidelineFileIds, Logo = kept.Logo };
            if (logo is not null)
            {
                db.Files.Add(logo);
                content = content with { Logo = new KitLogo { FileId = logo.Id, SourceUrl = outcome.Logo!.Url.AbsoluteUri } };
            }

            var before = draft is null ? null : KitVersions.AuditOf(draft);
            if (draft is null)
            {
                draft = await versions.CreateDraftAsync(content, VersionOrigin.Drafted, null, cancellationToken);
            }
            else
            {
                await versions.DraftIntoAsync(draft, content, cancellationToken);
            }

            Finish(run, DraftRunStatus.Succeeded, null, null, reading, ads);
            run.Model = Cut(answer.Model!, 100);
            run.PromptVersion = Cut(answer.PromptVersion!, 50);
            run.VersionNumber = draft.Number;
            await db.SaveChangesAsync(cancellationToken);

            await audit.RecordAsync(
                new AuditRecord("brand_kit.draft_saved", KitVersions.AuditTarget, KitVersions.Number(draft), before, KitVersions.AuditOf(draft)),
                cancellationToken);
            await live.PublishAsync(BrandKitLiveEvents.Drafting(run.Id, run.Status), cancellationToken);
            await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
            if (logo is not null)
            {
                await live.PublishAsync(BrandKitLiveEvents.FilesChanged(logo.Id), cancellationToken);
            }

            await notifications.NotifyAsync(
                new Notification(
                    NotificationKind,
                    LiveAudience.Users(run.RequestedBy),
                    ReadyTitle,
                    $"Kairo read {Count(reading.Pages.Count, "page")} and {Count(ads.Count, "ad")}. Review the draft, then approve it.",
                    Link(brand)),
                cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task FailAsync(
        Guid runId,
        DateTimeOffset lease,
        BrandSummary brand,
        string code,
        string message,
        WebsiteReading? reading,
        IReadOnlyList<BestAd> ads,
        CancellationToken cancellationToken)
    {
        logger.LogWarning("Drafting run {RunId} failed: {Code}.", runId, code);
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var run = await db.DraftRuns.SingleOrDefaultAsync(row => row.Id == runId && row.ClaimedUntil == lease, cancellationToken);
            if (run is null)
            {
                await database.RollbackAsync(cancellationToken);
                return;
            }

            Finish(run, DraftRunStatus.Failed, code, message, reading, ads);
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(BrandKitLiveEvents.Drafting(run.Id, run.Status), cancellationToken);
            await notifications.NotifyAsync(
                new Notification(NotificationKind, LiveAudience.Users(run.RequestedBy), FailedTitle, Cut(message, 1000), Link(brand)),
                cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task ReleaseAsync(Guid runId, DateTimeOffset lease, CancellationToken cancellationToken) =>
        db.DraftRuns
            .Where(row => row.Id == runId && row.ClaimedUntil == lease)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedUntil, (DateTimeOffset?)null), cancellationToken);

    private void Finish(DraftRun run, DraftRunStatus status, string? code, string? message, WebsiteReading? reading, IReadOnlyList<BestAd> ads)
    {
        IReadOnlyList<ReadPage> pages = reading?.Pages ?? [];
        run.Status = status;
        run.ErrorCode = code;
        run.ErrorMessage = message is null ? null : Cut(message, 500);
        run.ClaimedUntil = null;
        run.FinishedAt = time.GetUtcNow();
        run.Pages = JsonSerializer.Serialize(pages.Select(page => new DraftRunPage(page.Url.AbsoluteUri, page.Title)).ToArray(), KitJson.Options);
        run.Ads = JsonSerializer.Serialize(ads.Select(ad => new DraftRunAd(ad.Id, ad.Headlines.FirstOrDefault() ?? "")).ToArray(), KitJson.Options);
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Link(BrandSummary brand) => $"/{brand.Handle}/brand-kit";

    private static string Cut(string text, int max) => text.Length > max ? text[..max] : text;
}
