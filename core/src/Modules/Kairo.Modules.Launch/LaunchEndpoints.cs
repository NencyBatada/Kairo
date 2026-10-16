using Kairo.Contracts.Approvals;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Modules.Launch.Data;

namespace Kairo.Modules.Launch;

internal sealed record RetryLaunch(int Version);
internal sealed record LaunchStepView(string Name, int Attempt, string Outcome, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);
internal sealed record HealthView(string Status, DateTimeOffset DueAt, DateTimeOffset? CheckedAt, string? Cause, HealthAlert[] Alerts);
internal sealed record LaunchStatus(Guid Id, int Version, string Status, string Step, int Attempts, bool Uncertain,
    string? Cause, string? SuggestedFix, bool RetryAvailable, DateTimeOffset? NextAttemptAt, DateTimeOffset? ActivatedAt,
    Dictionary<string, string> ExternalIds, Dictionary<string, string> VideoIds, LaunchStepView[] Steps, HealthView? Health);

internal static class LaunchEndpoints
{
    public static async Task<Results<Ok<LaunchStatus>, NotFound>> GetAsync(Guid campaignId, LaunchDbContext db, ILaunchPlans plans, TimeProvider time, CancellationToken cancellationToken)
    {
        var record = await db.Launches.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == campaignId && row.Status != "superseded", cancellationToken);
        return record is null ? TypedResults.NotFound() : TypedResults.Ok(await ViewAsync(record, db, plans, time, cancellationToken));
    }

    public static async Task<Results<Ok<LaunchStatus>, NotFound, ProblemHttpResult>> RetryAsync(Guid campaignId, RetryLaunch request,
        LaunchDbContext db, KairoDatabase database, ILaunchPlans plans, TimeProvider time, IAuditLog audit, ILiveUpdates live, CancellationToken cancellationToken)
    {
        await database.LockAsync($"room:{campaignId:N}", cancellationToken);
        await database.LockAsync($"launch:{campaignId:N}", cancellationToken);
        var record = await db.Launches.SingleOrDefaultAsync(row => row.RoomId == campaignId && row.Status != "superseded", cancellationToken);
        if (record is null) return TypedResults.NotFound();
        if (record.Version != request.Version) return Conflict("Launch progress changed. Refresh before retrying.");
        if (!Available(record, time)) return Conflict("This launch is running, already live, or waiting for its retry delay.");
        var plan = await plans.GetAsync(campaignId, cancellationToken);
        if (plan is null || ApprovalHash.Of(LaunchRunner.Write(plan)) != record.Hash || !await plans.IsApprovedAsync(campaignId, cancellationToken))
            return Conflict("The current launch plan needs fresh approval before retrying.");
        record.Status = "queued";
        record.NextAttemptAt = null;
        record.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("launch.retry_requested", "campaign_room", campaignId.ToString(), null, new { record.Id, record.Step }), cancellationToken);
        await live.PublishAsync(new LiveEvent("launch.changed", new { campaignId, record.Status, record.Step }, LiveAudience.Everyone), cancellationToken);
        return TypedResults.Ok(await ViewAsync(record, db, plans, time, cancellationToken));
    }

    private static bool Available(LaunchRecord record, TimeProvider time) => record.Status is "failed" or "retry_wait"
        && record.ClaimToken is null && (record.NextAttemptAt is null || record.NextAttemptAt <= time.GetUtcNow());

    private static async Task<LaunchStatus> ViewAsync(LaunchRecord record, LaunchDbContext db, ILaunchPlans plans, TimeProvider time, CancellationToken cancellationToken)
    {
        var steps = await db.Steps.Where(row => row.LaunchId == record.Id).OrderBy(row => row.StartedAt).ThenBy(row => row.Attempt)
            .Select(row => new LaunchStepView(row.Name, row.Attempt, row.Outcome, row.StartedAt, row.FinishedAt)).ToArrayAsync(cancellationToken);
        var health = await db.Health.AsNoTracking().SingleOrDefaultAsync(row => row.LaunchId == record.Id, cancellationToken);
        var current = Available(record, time) ? await plans.GetAsync(record.RoomId, cancellationToken) : null;
        var retry = current is not null && ApprovalHash.Of(LaunchRunner.Write(current)) == record.Hash && await plans.IsApprovedAsync(record.RoomId, cancellationToken);
        var uploads = LaunchRunner.Read<Dictionary<string, Google.VideoUpload>>(record.Uploads);
        return new LaunchStatus(record.Id, record.Version, record.Status, record.Step, record.Attempts, record.Uncertain,
            record.ErrorMessage, record.SuggestedFix, retry, record.NextAttemptAt, record.ActivatedAt,
            LaunchRunner.Read<Dictionary<string, string>>(record.Resources).Where(pair => pair.Key != "built").ToDictionary(),
            uploads.Where(pair => pair.Value.VideoId is not null).ToDictionary(pair => pair.Key, pair => pair.Value.VideoId!), steps,
            health is null ? null : new HealthView(health.Status, health.DueAt, health.CheckedAt, health.Cause, LaunchRunner.Read<HealthAlert[]>(health.Alerts)));
    }

    private static ProblemHttpResult Conflict(string message) => TypedResults.Problem(statusCode: 409, title: message);
}
