using System.Text.Json;
using System.Diagnostics;
using Kairo.SharedKernel.Observability;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Stages;

internal sealed class StageRunner(
    CampaignRoomsDbContext db,
    KairoDatabase database,
    StageQueue queue,
    RoomWorkflow workflow,
    IAuditLog audit,
    IAgentProgress progress,
    IOperationsAlerts operations,
    TimeProvider time,
    ILogger<StageRunner> logger,
    AgentUsageContext usage)
{
    public const int MaxAttempts = 3;
    public const string FailedMessage = "The agent couldn't finish this step. Try again.";

    public static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    public async Task RunAsync(Guid runId, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var run = await db.StageRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == runId && row.ClaimedUntil == lease, cancellationToken);
        if (run is null)
        {
            return;
        }

        using var activity = KairoTelemetry.StartStage(run.Kind, run.TraceParent, run.BrandId, run.RoomId, run.Id);
        var started = Stopwatch.GetTimestamp();

        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == run.RoomId, cancellationToken);
        if (room is null || room.State != run.Stage)
        {
            await CancelAsync(run.Id, lease, cancellationToken);
            return;
        }

        using var usageScope = usage.Enter(new AgentUsageScope(run.BrandId, room.Id, run.Id));
        var work = queue.Find(run.Kind);
        if (run.Attempts > (work?.AttemptsFor(run) ?? MaxAttempts))
        {
            await FailAsync(run, lease, StageFailure.Crashed, FailedMessage, cancellationToken);
            return;
        }

        if (work is null)
        {
            await FailAsync(run, lease, StageFailure.UnknownKind, FailedMessage, cancellationToken);
            return;
        }

        await progress.ReportAsync(StageProgress.Of(run, AgentJobStatus.Running, "Working"), cancellationToken);
        StageOutcome outcome;
        try
        {
            outcome = await work.RunAsync(run, room, cancellationToken);
        }
        catch (StageFailure failure)
        {
            KairoTelemetry.StageFinished(run.Kind, "failed", Stopwatch.GetElapsedTime(started).TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Error);
            await AlertSecondFailureAsync(run, cancellationToken);
            await FailAsync(run, lease, failure.Code, failure.Message, cancellationToken);
            return;
        }
        catch (AgentCallException exception)
        {
            KairoTelemetry.StageFinished(run.Kind, "failed", Stopwatch.GetElapsedTime(started).TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Error);
            await AlertSecondFailureAsync(run, cancellationToken);
            logger.LogWarning(exception, "Stage run {RunId} ({Kind}) failed on attempt {Attempt}.", run.Id, run.Kind, run.Attempts);
            if (exception.Code is AgentCallException.NotConfigured or AgentCallException.GenerationLimitExceeded || run.Attempts >= work.AttemptsFor(run))
            {
                await FailAsync(run, lease, exception.Code, FailedMessage, cancellationToken);
            }
            else
            {
                await ReleaseAsync(run.Id, lease, run.Attempts, cancellationToken);
            }

            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            KairoTelemetry.StageFinished(run.Kind, "failed", Stopwatch.GetElapsedTime(started).TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Error);
            await AlertSecondFailureAsync(run, cancellationToken);
            logger.LogError(exception, "Stage run {RunId} ({Kind}) stopped unexpectedly on attempt {Attempt}.", run.Id, run.Kind, run.Attempts);
            db.ChangeTracker.Clear();
            if (run.Attempts >= work.AttemptsFor(run))
            {
                await FailAsync(run, lease, StageFailure.Crashed, FailedMessage, cancellationToken);
            }
            else
            {
                await ReleaseAsync(run.Id, lease, run.Attempts, cancellationToken);
            }

            return;
        }

        await SucceedAsync(run, work, lease, outcome, cancellationToken);
        KairoTelemetry.StageFinished(run.Kind, "succeeded", Stopwatch.GetElapsedTime(started).TotalSeconds);
    }

    private async Task AlertSecondFailureAsync(StageRun run, CancellationToken cancellationToken)
    {
        if (run.Attempts != 2) return;
        KairoTelemetry.Event(logger, "stage.failed_twice", run.BrandId, run.RoomId, run.Id);
        await operations.QueueAsync(new OperationsAlert(run.Id, "stage.failed_twice", run.RoomId, run.Id), cancellationToken);
    }

    private async Task SucceedAsync(StageRun claimed, IStageWork work, DateTimeOffset lease, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var saved = await InTransactionAsync(async () =>
        {
            if (await LockedAsync(claimed, lease, cancellationToken) is not ({ } run, { } room))
            {
                return null;
            }

            if (room.State != run.Stage)
            {
                Cancel(run);
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }

            await work.SaveAsync(run, room, outcome, cancellationToken);
            run.Status = StageRunStatuses.Succeeded;
            run.FinishedAt = time.GetUtcNow();
            run.ClaimedUntil = null;
            run.ErrorCode = null;
            run.ErrorMessage = null;
            run.Model = outcome.Model;
            run.PromptVersion = outcome.PromptVersion;
            run.InputTokens = outcome.Usage?.InputTokens;
            run.OutputTokens = outcome.Usage?.OutputTokens;
            run.Output = JsonSerializer.Serialize(outcome.Output, JsonSerializerOptions.Web);
            room.RunStatus = run.Status;
            if (work.NextKind(run, outcome) is { } next && queue.Find(next) is not null)
            {
                await queue.EnqueueAsync(room, next, run.RequestedBy, cancellationToken, work.NextInput(run, outcome));
            }

            workflow.Touch(room);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord(
                    "stage_run.succeeded",
                    RoomWorkflow.AuditTarget,
                    room.Id.ToString(),
                    After: new { runId = run.Id, run.Kind, run.Model, run.PromptVersion, run.InputTokens, run.OutputTokens, summary = outcome.Summary },
                    Actor: AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
                cancellationToken);
            await workflow.PublishAsync(room, cancellationToken);
            return run;
        }, cancellationToken);
        if (saved is not null)
        {
            await progress.ReportAsync(StageProgress.Of(saved, AgentJobStatus.Succeeded, "Done", outcome.Summary), cancellationToken);
        }
    }

    private async Task FailAsync(StageRun claimed, DateTimeOffset lease, string code, string message, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var failed = await InTransactionAsync(async () =>
        {
            if (await LockedAsync(claimed, lease, cancellationToken) is not ({ } run, { } room))
            {
                return null;
            }

            run.Status = StageRunStatuses.Failed;
            run.FinishedAt = time.GetUtcNow();
            run.ClaimedUntil = null;
            run.ErrorCode = code;
            run.ErrorMessage = message.Length > 500 ? message[..500] : message;
            if (room.State == run.Stage)
            {
                room.RunStatus = run.Status;
                workflow.Touch(room);
            }

            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord(
                    "stage_run.failed",
                    RoomWorkflow.AuditTarget,
                    room.Id.ToString(),
                    After: new { runId = run.Id, run.Kind, run.ErrorCode, run.Attempts },
                    Actor: AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
                cancellationToken);
            await workflow.PublishAsync(room, cancellationToken);
            return run;
        }, cancellationToken);
        if (failed is not null)
        {
            await progress.ReportAsync(StageProgress.Of(failed, AgentJobStatus.Failed, "Failed", failed.ErrorMessage), cancellationToken);
        }
    }

    private async Task CancelAsync(Guid runId, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.StageRuns
            .Where(run => run.Id == runId && run.ClaimedUntil == lease)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, StageRunStatuses.Cancelled)
                    .SetProperty(run => run.FinishedAt, now)
                    .SetProperty(run => run.ClaimedUntil, (DateTimeOffset?)null),
                cancellationToken);
    }

    private async Task ReleaseAsync(Guid runId, DateTimeOffset lease, int attempts, CancellationToken cancellationToken)
    {
        var next = time.GetUtcNow() + Backoff[Math.Clamp(attempts - 1, 0, Backoff.Length - 1)];
        await db.StageRuns
            .Where(run => run.Id == runId && run.ClaimedUntil == lease)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, StageRunStatuses.Queued)
                    .SetProperty(run => run.NextAttemptAt, next)
                    .SetProperty(run => run.ClaimedUntil, (DateTimeOffset?)null),
                cancellationToken);
    }

    private void Cancel(StageRun run)
    {
        run.Status = StageRunStatuses.Cancelled;
        run.FinishedAt = time.GetUtcNow();
        run.ClaimedUntil = null;
    }

    private async Task<(StageRun?, Room?)> LockedAsync(StageRun claimed, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var room = await workflow.LockedAsync(claimed.RoomId, cancellationToken);
        var run = await db.StageRuns.SingleOrDefaultAsync(
            row => row.Id == claimed.Id && row.ClaimedUntil == lease && row.Status == StageRunStatuses.Running,
            cancellationToken);
        return (run, room);
    }

    private async Task<T?> InTransactionAsync<T>(Func<Task<T?>> action, CancellationToken cancellationToken)
        where T : class
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action();
            await database.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
