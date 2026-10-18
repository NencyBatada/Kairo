using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Kairo.SharedKernel.Observability;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Launch;

internal sealed class LaunchFailureRecorder(LaunchDbContext db, KairoDatabase database, ICampaignLifecycle lifecycle,
    IAuditLog audit, ILiveUpdates live, INotifications notifications, IOperationsAlerts operations, IBrandDirectory brands, TimeProvider time, ILogger<LaunchFailureRecorder> logger)
{
    public async Task RecordAsync(Guid id, Guid claim, LaunchException error, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var record = await db.Launches.SingleAsync(row => row.Id == id, cancellationToken);
            await database.LockAsync($"room:{record.RoomId:N}", cancellationToken);
            await database.LockAsync($"launch:{record.RoomId:N}", cancellationToken);
            await db.Entry(record).ReloadAsync(cancellationToken);
            if (record.ClaimToken != claim)
            {
                await database.RollbackAsync(cancellationToken);
                return;
            }

            record.Status = "failed";
            record.ErrorCode = error.Code;
            record.ErrorMessage = error.Message;
            record.SuggestedFix = error.Fix;
            record.Uncertain = error.Uncertain;
            record.Version++;
            record.NextAttemptAt = null;
            if (error.Transient && record.RetryCount < 3)
            {
                record.NextAttemptAt = time.GetUtcNow() + RetryDelay(record.RetryCount++);
                record.Status = "retry_wait";
            }
            var step = await db.Steps.Where(row => row.LaunchId == id && row.FinishedAt == null).OrderByDescending(row => row.StartedAt).FirstOrDefaultAsync(cancellationToken);
            if (step is not null)
            {
                step.Outcome = error.Uncertain ? "uncertain" : "failed";
                step.FinishedAt = time.GetUtcNow();
            }
            if (!error.Uncertain) record.AttemptingStep = null;
            record.ClaimedUntil = null;
            record.ClaimToken = null;
            await db.SaveChangesAsync(cancellationToken);
            await lifecycle.AdvanceAsync(record.RoomId, CampaignSignals.LaunchFailed, LaunchRunner.Actor, cancellationToken);
            await audit.RecordAsync(new AuditRecord("launch.failed", "campaign_room", record.RoomId.ToString(), null,
                new { step = record.Step, error.Code, error.Uncertain }, LaunchRunner.Actor), cancellationToken);
            var brand = (await brands.GetAsync([record.BrandId], cancellationToken)).Single();
            await notifications.NotifyAsync(new Notification("launch.failed", LiveAudience.Roles(BrandRoles.Marketer), "Campaign launch needs attention",
                error.Message + " " + error.Fix, $"/{brand.Handle}/campaigns/{record.RoomId}/launch", $"launch:{record.RoomId:N}:launch.failed"), cancellationToken);
            await live.PublishAsync(new LiveEvent("launch.changed", new { campaignId = record.RoomId, record.Status, record.Step }, LiveAudience.Everyone), cancellationToken);
            await operations.QueueAsync(new OperationsAlert(claim, "launch.failed", record.RoomId, record.Id), cancellationToken);
            await database.CommitAsync(cancellationToken);
            KairoTelemetry.Event(logger, "launch.failed", record.BrandId, record.RoomId, record.Id);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(attempt switch { 0 => 30, 1 => 120, _ => 600 });
}
