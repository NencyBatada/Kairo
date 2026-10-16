using System.Globalization;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Launch;

internal sealed record HealthAlert(string Kind, string Resource, string Cause, string SuggestedFix);

internal sealed class LaunchHealthCheck(LaunchDbContext db, KairoDatabase database, IServiceScopeFactory scopes, TimeProvider time, ILogger<LaunchHealthCheck> logger)
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var until = now + LaunchRunner.Lease;
        var claim = Guid.NewGuid();
        List<ClaimedLaunch> claimed;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
            claimed = await db.Database.SqlQuery<ClaimedLaunch>($"""
                UPDATE TOP (4) launch.Health WITH (READPAST, UPDLOCK, ROWLOCK)
                SET ClaimToken = {claim}, ClaimedUntil = {until}, Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.BrandId
                WHERE Status = N'pending' AND DueAt <= {now} AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
                """).ToListAsync(cancellationToken);
        foreach (var row in claimed)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(row.BrandId, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<LaunchHealthWorker>().RunAsync(row.Id, claim, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Health check {HealthId} stopped; its lease will expire before retry.", row.Id);
            }
        }
        return claimed.Count;
    }
}

internal sealed class LaunchHealthWorker(LaunchDbContext db, KairoDatabase database, LaunchGoogle google, TimeProvider time,
    INotifications notifications, IBrandDirectory brands, IAuditLog audit, ILiveUpdates live)
{
    public async Task RunAsync(Guid id, Guid claim, CancellationToken cancellationToken)
    {
        var health = await db.Health.SingleAsync(row => row.Id == id, cancellationToken);
        var record = await db.Launches.SingleAsync(row => row.Id == health.LaunchId, cancellationToken);
        var plan = LaunchRunner.Read<LaunchPlan>(record.Plan);
        HealthAlert[] alerts;
        LaunchException? error = null;
        try { alerts = await CheckAsync(record, plan, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            alerts = [];
            error = exception as LaunchException ?? new LaunchException("health_unavailable", "Google campaign health couldn't be checked.", "Reconnect the approved account and inspect campaign approvals and delivery in Google Ads.");
        }
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"launch-health:{id:N}", cancellationToken);
            await db.Entry(health).ReloadAsync(cancellationToken);
            if (health.ClaimToken != claim || health.ClaimedUntil <= time.GetUtcNow()) { await database.RollbackAsync(cancellationToken); return; }
            health.ClaimToken = null;
            health.ClaimedUntil = null;
            health.Cause = error?.Message;
            if (error is { Transient: true } && health.Attempts < 4)
                health.DueAt = time.GetUtcNow() + LaunchFailureRecorder.RetryDelay(health.Attempts - 1);
            else
            {
                health.Status = error is null ? "complete" : "failed";
                health.CheckedAt = time.GetUtcNow();
                if (error is not null) alerts = [new HealthAlert("check_unavailable", plan.GoogleAdsAccountId!, error.Message, error.Fix)];
                health.Alerts = LaunchRunner.Write(alerts);
                var brand = (await brands.GetAsync([record.BrandId], cancellationToken)).Single();
                foreach (var alert in alerts.DistinctBy(alert => (alert.Kind, alert.Resource)))
                    await notifications.NotifyAsync(new Notification("health.problem", LiveAudience.Roles(BrandRoles.Marketer),
                        "Campaign health needs attention", alert.Cause + " " + alert.SuggestedFix, $"/{brand.Handle}/campaigns/{record.RoomId}/launch",
                        $"launch:{record.Id:N}:health:{alert.Kind}:{alert.Resource}"), cancellationToken);
                await audit.RecordAsync(new AuditRecord("launch.health_checked", "campaign_room", record.RoomId.ToString(), null,
                    new { health.Status, alerts }, LaunchRunner.Actor), cancellationToken);
                await live.PublishAsync(new LiveEvent("launch.changed", new { campaignId = record.RoomId }, LiveAudience.Everyone), cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch { await database.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<HealthAlert[]> CheckAsync(LaunchRecord record, LaunchPlan plan, CancellationToken cancellationToken)
    {
        var resources = LaunchRunner.Read<Dictionary<string, string>>(record.Resources);
        var campaignNames = plan.Settings.Channels.Select(channel => resources["campaign:" + channel.CampaignType]).ToArray();
        var filter = string.Join(',', campaignNames.Select(name => "'" + name + "'"));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(plan.TimeZone)).DateTime);
        var alerts = new List<HealthAlert>();
        var from = today < plan.Start ? today : plan.Start;
        var campaigns = await google.SearchAsync(plan.GoogleAdsAccountId!, $"SELECT campaign.resource_name, campaign.status, campaign.primary_status, campaign.primary_status_reasons, metrics.impressions FROM campaign WHERE campaign.resource_name IN ({filter}) AND segments.date BETWEEN '{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' AND '{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'", cancellationToken);
        foreach (var name in campaignNames)
        {
            var matches = campaigns.Where(row => LaunchPreflight.Text(row.GetProperty("campaign"), "resourceName") == name).ToArray();
            if (matches.Length != 1)
            {
                if (today >= plan.Start) alerts.Add(new("delivery", name, "Google returned no delivery information for this campaign.", "Check that the campaign exists, is enabled, and has eligible ads and products."));
                continue;
            }
            var row = matches[0];
            if (today >= plan.Start && (!row.TryGetProperty("metrics", out var metrics) || !long.TryParse(LaunchPreflight.Text(metrics, "impressions"), out var impressions) || impressions == 0))
            {
                var campaign = row.GetProperty("campaign");
                var reasons = campaign.TryGetProperty("primaryStatusReasons", out var statusReasons) ? string.Join(", ", statusReasons.EnumerateArray().Select(reason => reason.GetString()?.Replace('_', ' ').ToLowerInvariant())) : "no recorded impressions";
                alerts.Add(new("delivery", name, "This campaign has no delivery: " + reasons + ".", "Check campaign status, billing, approved bids, targeting, eligible products and conversion tracking in Google Ads."));
            }
        }
        var ads = await google.SearchAsync(plan.GoogleAdsAccountId!, $"SELECT ad_group_ad.resource_name, ad_group_ad.policy_summary.approval_status, ad_group_ad.policy_summary.policy_topic_entries FROM ad_group_ad WHERE campaign.resource_name IN ({filter}) AND ad_group_ad.status != 'REMOVED'", cancellationToken);
        foreach (var row in ads)
        {
            var ad = row.GetProperty("adGroupAd");
            if (!ad.TryGetProperty("policySummary", out var policy) || LaunchPreflight.Text(policy, "approvalStatus") != "DISAPPROVED") continue;
            var reasons = policy.TryGetProperty("policyTopicEntries", out var entries) ? string.Join(", ", entries.EnumerateArray().Select(entry => LaunchPreflight.Text(entry, "topic").Replace('_', ' ').ToLowerInvariant())) : "Google Ads policy review";
            alerts.Add(new("disapproval", LaunchPreflight.Text(ad, "resourceName"), "Google disapproved this ad: " + reasons + ".", "Open the policy details in Google Ads, correct the flagged creative or destination, and request review. Any new Kairo creative needs fresh approval."));
        }
        return [.. alerts.DistinctBy(alert => (alert.Kind, alert.Resource))];
    }
}
