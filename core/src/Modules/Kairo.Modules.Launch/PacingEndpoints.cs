using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;

namespace Kairo.Modules.Launch;

internal sealed record PacingDayView(DateOnly Date, decimal Spend, decimal PlannedSpend, decimal Deviation, string Status,
    decimal Conversions, decimal ConversionValue, long Impressions, long Clicks);
internal sealed record PacingAlertView(DateOnly Date, string Direction, string? CauseKind, string? Cause, string? SuggestedFix, DateTimeOffset AlertedAt);
internal sealed record PacingCampaignView(string CampaignType, decimal PlannedDailySpend, string Status, PacingDayView[] Days, PacingAlertView? Alert);
internal sealed record PacingView(string Status, string Currency, DateOnly Start, DateOnly End, PacingCampaignView[] Campaigns);

internal static class PacingEndpoints
{
    public static async Task<Results<Ok<PacingView>, NotFound>> GetAsync(Guid campaignId, LaunchDbContext db, CancellationToken cancellationToken)
    {
        var record = await db.Launches.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == campaignId && row.Status != "superseded", cancellationToken);
        if (record is null) return TypedResults.NotFound();
        var plan = LaunchRunner.Read<LaunchPlan>(record.Plan);
        var days = await db.Pacing.AsNoTracking().Where(row => row.LaunchId == record.Id).OrderBy(row => row.Date).ToListAsync(cancellationToken);
        var campaigns = plan.Settings.Channels.Select(channel =>
        {
            var rows = days.Where(row => row.CampaignType == channel.CampaignType).ToArray();
            var latest = rows.LastOrDefault();
            var alert = latest is { StreakAlerted: true } ? rows.LastOrDefault(row => row.AlertedAt is not null) : null;
            return new PacingCampaignView(channel.CampaignType, PacingRules.PlannedDaily(channel, plan), latest?.Status ?? "waiting",
                [.. rows.Select(row => new PacingDayView(row.Date, row.Spend, row.PlannedSpend, row.Deviation, row.Status, row.Conversions, row.ConversionValue, row.Impressions, row.Clicks))],
                alert is null ? null : new PacingAlertView(alert.Date, alert.Status, alert.CauseKind, alert.Cause, alert.SuggestedFix, alert.AlertedAt!.Value));
        }).ToArray();
        var status = campaigns.All(campaign => campaign.Status == "waiting") ? "waiting"
            : campaigns.Any(campaign => campaign.Alert is not null) ? "off_pace" : "on_pace";
        return TypedResults.Ok(new PacingView(status, plan.Currency, plan.Start, plan.End, campaigns));
    }
}
