using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Kairo.SharedKernel.Access;

namespace Kairo.Modules.Launch;

internal sealed record StockPauseChange(int Version);
internal sealed record StockPauseView(Guid Id, int Version, string State, string CampaignType, string CampaignResource, string AdResource,
    string[] ProductIds, Guid? BatchId, DateTimeOffset CreatedAt, DateTimeOffset? PausedAt, DateTimeOffset? ResumedAt,
    DateTimeOffset? DecidedAt, Guid? DecidedBy, bool CanUndo);
internal sealed record StockPauseLog(DateTimeOffset? FeedUpdatedAt, bool FeedStale, StockPauseView[] Pauses);

internal static class StockPauseEndpoints
{
    public static async Task<Ok<StockPauseLog>> ListAsync(Guid campaignId, LaunchDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await LogAsync(campaignId, db, cancellationToken));

    public static async Task<Results<Ok<StockPauseLog>, NotFound, ProblemHttpResult>> UndoAsync(Guid campaignId, Guid pauseId, StockPauseChange request,
        LaunchDbContext db, KairoDatabase database, LaunchGoogle google, CurrentMember member, TimeProvider time, IAuditLog audit, ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await database.LockAsync($"launch-stock:{database.BrandId:N}", cancellationToken);
        var pause = await db.AdPauses.SingleOrDefaultAsync(row => row.Id == pauseId && row.RoomId == campaignId, cancellationToken);
        if (pause is null) return TypedResults.NotFound();
        if (pause.Version != request.Version) return Conflict("This pause changed. Refresh before undoing it.");
        if (pause.State != PauseStates.Paused) return Conflict("Only an ad that is still paused for stock can be resumed.");
        var plan = await PlanAsync(db, pause.LaunchId, cancellationToken);
        if (await SendAsync(google, plan, [(pause.AdResource, "ENABLED")], cancellationToken) is { } failure) return failure;
        pause.State = PauseStates.Undone;
        pause.ResumedAt = pause.DecidedAt = time.GetUtcNow();
        pause.DecidedBy = member.UserId;
        pause.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("launch.stock_pause_undone", "campaign_room", campaignId.ToString(), null,
            new { pause.Id, pause.AdResource, pause.CampaignType }), cancellationToken);
        await live.PublishAsync(new LiveEvent("launch.stock_changed", new { campaignId }, LiveAudience.Everyone), cancellationToken);
        return TypedResults.Ok(await LogAsync(campaignId, db, cancellationToken));
    }

    public static Task<Results<Ok<StockPauseLog>, NotFound, ProblemHttpResult>> ConfirmAsync(Guid campaignId, Guid batchId,
        LaunchDbContext db, KairoDatabase database, LaunchGoogle google, CurrentMember member, TimeProvider time, IAuditLog audit, ILiveUpdates live,
        INotifications notifications, CancellationToken cancellationToken) =>
        DecideAsync(campaignId, batchId, true, db, database, google, member, time, audit, live, notifications, cancellationToken);

    public static Task<Results<Ok<StockPauseLog>, NotFound, ProblemHttpResult>> DismissAsync(Guid campaignId, Guid batchId,
        LaunchDbContext db, KairoDatabase database, LaunchGoogle google, CurrentMember member, TimeProvider time, IAuditLog audit, ILiveUpdates live,
        INotifications notifications, CancellationToken cancellationToken) =>
        DecideAsync(campaignId, batchId, false, db, database, google, member, time, audit, live, notifications, cancellationToken);

    private static async Task<Results<Ok<StockPauseLog>, NotFound, ProblemHttpResult>> DecideAsync(Guid campaignId, Guid batchId, bool confirm,
        LaunchDbContext db, KairoDatabase database, LaunchGoogle google, CurrentMember member, TimeProvider time, IAuditLog audit, ILiveUpdates live,
        INotifications notifications, CancellationToken cancellationToken)
    {
        await database.LockAsync($"launch-stock:{database.BrandId:N}", cancellationToken);
        var rows = await db.AdPauses.Where(row => row.BatchId == batchId && row.RoomId == campaignId).ToListAsync(cancellationToken);
        if (rows.Count == 0) return TypedResults.NotFound();
        var held = rows.Where(row => row.State == PauseStates.Held).ToList();
        if (held.Count == 0) return Conflict("These pauses were already decided.");
        if (confirm)
        {
            var plan = await PlanAsync(db, held[0].LaunchId, cancellationToken);
            if (await SendAsync(google, plan, [.. held.Select(row => (row.AdResource, "PAUSED"))], cancellationToken) is { } failure) return failure;
        }

        var now = time.GetUtcNow();
        foreach (var row in held)
        {
            row.State = confirm ? PauseStates.Paused : PauseStates.Dismissed;
            row.PausedAt = confirm ? now : null;
            row.DecidedAt = now;
            row.DecidedBy = member.UserId;
            row.Version++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(confirm ? "launch.mass_pause_confirmed" : "launch.mass_pause_dismissed", "campaign_room", campaignId.ToString(), null,
            new { batchId, ads = held.Select(row => row.AdResource).ToArray() }), cancellationToken);
        await notifications.ResolveAsync(StockPauseWorker.Subject(batchId), cancellationToken);
        await live.PublishAsync(new LiveEvent("launch.stock_changed", new { campaignId }, LiveAudience.Everyone), cancellationToken);
        return TypedResults.Ok(await LogAsync(campaignId, db, cancellationToken));
    }

    private static async Task<ProblemHttpResult?> SendAsync(LaunchGoogle google, LaunchPlan plan, (string, string)[] changes, CancellationToken cancellationToken)
    {
        try
        {
            await StockPauseWorker.SetStatusAsync(google, plan.GoogleAdsAccountId!, changes, cancellationToken);
            return null;
        }
        catch (LaunchException exception)
        {
            return TypedResults.Problem(statusCode: 502, title: exception.Message, detail: exception.Fix);
        }
        catch (ConnectionUnavailableException)
        {
            return TypedResults.Problem(statusCode: 502, title: "The Google Ads account needs reconnecting.", detail: "Reconnect Google Ads in Settings and try again.");
        }
    }

    private static async Task<LaunchPlan> PlanAsync(LaunchDbContext db, Guid launchId, CancellationToken cancellationToken) =>
        LaunchRunner.Read<LaunchPlan>((await db.Launches.AsNoTracking().SingleAsync(row => row.Id == launchId, cancellationToken)).Plan);

    private static async Task<StockPauseLog> LogAsync(Guid campaignId, LaunchDbContext db, CancellationToken cancellationToken)
    {
        var watch = await db.StockWatches.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var rows = await db.AdPauses.AsNoTracking().Where(row => row.RoomId == campaignId)
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.AdResource).ToListAsync(cancellationToken);
        return new StockPauseLog(watch?.FeedUpdatedAt, watch?.StaleSince is not null, [.. rows.Select(row => new StockPauseView(row.Id, row.Version, row.State,
            row.CampaignType, row.CampaignResource, row.AdResource, LaunchRunner.Read<string[]>(row.ProductIds), row.BatchId, row.CreatedAt,
            row.PausedAt, row.ResumedAt, row.DecidedAt, row.DecidedBy, row.State == PauseStates.Paused))]);
    }

    private static ProblemHttpResult Conflict(string message) => TypedResults.Problem(statusCode: 409, title: message);
}
