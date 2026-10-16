using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Launch;

internal static class PacingStates
{
    public const string OnPace = "on_pace";
    public const string Over = "over";
    public const string Under = "under";
}

internal sealed record CampaignDay(decimal Spend, decimal Conversions, decimal ConversionValue, long Impressions, long Clicks);

internal sealed record PacingSignals(string[] Reasons, int Ads, int BlockedAds, decimal? RankLostShare, decimal? RecentCpc, decimal? EarlierCpc, int StockPausedAds = 0);

internal sealed record PacingCause(string Kind, string Text, string Fix);

internal sealed record PacingWindow(DateOnly First, DateOnly Last);

internal static class PacingRules
{
    public const decimal Tolerance = 0.2m;
    public const int StreakDays = 2;
    public const int CatchUpDays = 7;
    public static readonly TimeSpan ReadyAfter = TimeSpan.FromHours(6);

    public static decimal PlannedDaily(LaunchChannel channel, LaunchPlan plan) =>
        decimal.Round(channel.Budget / (plan.End.DayNumber - plan.Start.DayNumber + 1), 4);

    public static decimal Deviation(decimal spend, decimal planned) =>
        planned <= 0 ? 0 : Math.Clamp(decimal.Round((spend - planned) / planned, 4), -1m, 9999m);

    public static string Status(decimal deviation) =>
        deviation > Tolerance ? PacingStates.Over : deviation < -Tolerance ? PacingStates.Under : PacingStates.OnPace;

    public static PacingWindow? Window(LaunchPlan plan, DateTimeOffset activatedAt, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plan.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var ready = DateOnly.FromDateTime(local.DateTime).AddDays(local.TimeOfDay >= ReadyAfter ? -1 : -2);
        var launchDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(activatedAt, zone).DateTime);
        var last = ready < plan.End ? ready : plan.End;
        var first = new[] { plan.Start, launchDay.AddDays(1), last.AddDays(1 - CatchUpDays) }.Max();
        return first > last ? null : new PacingWindow(first, last);
    }

    public static PacingCause Cause(string status, PacingSignals signals)
    {
        bool Has(params string[] reasons) => signals.Reasons.Intersect(reasons).Any();
        if (status == PacingStates.Over)
        {
            if (signals is { RecentCpc: { } recent, EarlierCpc: > 0 and var earlier } && recent >= earlier * 1.2m)
                return new("costlier_clicks", $"Clicks cost {Percent((recent - earlier) / earlier)}% more than earlier in the campaign.",
                    "Check new competitors and broad keyword matches, or ask for a lower bid target.");
            return new("high_demand", "Demand is higher than planned, so Google is spending the total budget faster.",
                "Expect the campaign to finish its budget early, or ask for a budget move once the test ends.");
        }
        if (Has("CAMPAIGN_PAUSED", "CAMPAIGN_ENDED", "CAMPAIGN_REMOVED"))
            return new("campaign_stopped", "The campaign is paused or has ended in Google Ads, so it can't spend its plan.",
                "Resume the campaign in Kairo or Google Ads if it should still be running.");
        if (signals.StockPausedAds > 0 && (signals.Ads == 0 || signals.StockPausedAds * 4 >= signals.Ads))
            return new("stock_paused", signals.Ads > 0
                    ? $"Kairo paused {signals.StockPausedAds} of {signals.Ads} ads because their products sold out, so fewer ads can serve."
                    : "Kairo paused this campaign's ads because their products sold out, so fewer ads can serve.",
                "The ads resume automatically when stock returns. Undo a pause from the stock pause log if the product can still be sold.");
        if (signals.Ads > 0 && signals.BlockedAds * 4 >= signals.Ads)
            return new("ads_unavailable", $"{signals.BlockedAds} of {signals.Ads} ads are paused or disapproved" + (signals.StockPausedAds > 0 ? $" ({signals.StockPausedAds} paused by Kairo for stock)" : "") + ", so fewer ads can serve.",
                "Check stock pauses and disapproved ads, and fix or replace the ads that can't serve.");
        if (Has("AD_GROUP_ADS_PAUSED", "NO_AD_GROUP_ADS", "HAS_ADS_DISAPPROVED", "MOST_ADS_UNDER_REVIEW"))
            return new("ads_unavailable", "Most of the campaign's ads are paused, disapproved or still under review.",
                "Check stock pauses and disapproved ads, and fix or replace the ads that can't serve.");
        if (Has("BIDDING_STRATEGY_LIMITED", "BIDDING_STRATEGY_CONSTRAINED", "BIDDING_STRATEGY_MISCONFIGURED") || signals.RankLostShare >= 0.3m)
            return new("target_too_strict", signals.RankLostShare is { } lost and >= 0.3m
                    ? $"The bid target is too strict to win enough auctions: the campaign missed {Percent(lost)}% of eligible searches on ad rank."
                    : "The bid target is too strict to win enough auctions.",
                "Consider a less strict target CPA or target ROAS after the test, or a bid-target recommendation.");
        if (Has("BIDDING_STRATEGY_LEARNING"))
            return new("learning", "Google's bid strategy is still learning, so delivery is uneven.",
                "Give the campaign a few more days before changing it.");
        return new("low_demand", "Fewer people are searching for or browsing these products than planned.",
            "Check search trends for these products; the plan may have expected more demand than there is.");
    }

    private static string Percent(decimal share) => decimal.Round(share * 100, 0).ToString(CultureInfo.InvariantCulture);
}

internal sealed class PacingCheck(LaunchDbContext db, KairoDatabase database, IServiceScopeFactory scopes, ILogger<PacingCheck> logger)
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        List<ClaimedLaunch> live;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
            live = await db.Launches.AsNoTracking().Where(row => row.Status == "live" && row.ActivatedAt != null)
                .OrderBy(row => row.ActivatedAt).Select(row => new ClaimedLaunch { Id = row.Id, BrandId = row.BrandId }).ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var row in live)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(row.BrandId, cancellationToken);
                if (await scope.ServiceProvider.GetRequiredService<PacingWorker>().RunAsync(row.Id, cancellationToken)) processed++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Pacing check for launch {LaunchId} stopped; the next run will retry the missing days.", row.Id);
            }
        }
        return processed;
    }
}

internal sealed class PacingWorker(LaunchDbContext db, KairoDatabase database, LaunchGoogle google, TimeProvider time,
    INotifications notifications, IBrandMembers members, IApprovers approvers, IBrandDirectory brands, IAuditLog audit, ILiveUpdates live)
{
    public async Task<bool> RunAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await db.Launches.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        if (record.Status != "live" || record.ActivatedAt is not { } activatedAt) return false;
        var plan = LaunchRunner.Read<LaunchPlan>(record.Plan);
        if (PacingRules.Window(plan, activatedAt, time.GetUtcNow()) is not { } window) return false;
        var recorded = await RecordedDatesAsync(id, window, cancellationToken);
        var missing = Dates(window).Where(date => !recorded.Contains(date)).ToArray();
        if (missing.Length == 0) return false;
        var resources = LaunchRunner.Read<Dictionary<string, string>>(record.Resources);
        var campaigns = plan.Settings.Channels.ToDictionary(channel => channel.CampaignType, channel => resources["campaign:" + channel.CampaignType]);
        var metrics = await MetricsAsync(plan.GoogleAdsAccountId!, campaigns.Values, missing[0], missing[^1], cancellationToken);
        var history = await db.Pacing.AsNoTracking().Where(row => row.LaunchId == id).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var added = new List<PacingDay>();
        foreach (var date in missing)
            foreach (var channel in plan.Settings.Channels)
            {
                var day = metrics.GetValueOrDefault((campaigns[channel.CampaignType], date)) ?? new CampaignDay(0, 0, 0, 0, 0);
                var planned = PacingRules.PlannedDaily(channel, plan);
                var deviation = PacingRules.Deviation(day.Spend, planned);
                var status = PacingRules.Status(deviation);
                var previous = history.Concat(added).SingleOrDefault(row => row.CampaignType == channel.CampaignType && row.Date == date.AddDays(-1));
                var alert = status != PacingStates.OnPace && previous?.Status == status && !previous.StreakAlerted;
                added.Add(new PacingDay
                {
                    Id = Guid.NewGuid(),
                    LaunchId = id,
                    RoomId = record.RoomId,
                    CampaignType = channel.CampaignType,
                    Date = date,
                    Spend = day.Spend,
                    PlannedSpend = planned,
                    Conversions = day.Conversions,
                    ConversionValue = day.ConversionValue,
                    Impressions = day.Impressions,
                    Clicks = day.Clicks,
                    Deviation = deviation,
                    Status = status,
                    StreakAlerted = alert || status != PacingStates.OnPace && previous?.Status == status && previous.StreakAlerted,
                    AlertedAt = alert ? now : null,
                    RecordedAt = now,
                });
            }
        var alerts = added.Where(row => row.AlertedAt is not null).GroupBy(row => row.CampaignType).Select(group => group.MaxBy(row => row.Date)!).ToArray();
        foreach (var row in added.Where(row => row.AlertedAt is not null && !alerts.Contains(row))) row.AlertedAt = null;
        if (alerts.Length > 0)
        {
            var signals = await SignalsAsync(id, plan.GoogleAdsAccountId!, campaigns, alerts, history.Concat(added).ToArray(), cancellationToken);
            foreach (var row in alerts)
            {
                var cause = PacingRules.Cause(row.Status, signals[row.CampaignType]);
                (row.CauseKind, row.Cause, row.SuggestedFix) = (cause.Kind, cause.Text, cause.Fix);
            }
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"launch-pacing:{id:N}", cancellationToken);
            if ((await RecordedDatesAsync(id, window, cancellationToken)).Overlaps(missing)) { await database.RollbackAsync(cancellationToken); return false; }
            db.Pacing.AddRange(added);
            await db.SaveChangesAsync(cancellationToken);
            if (alerts.Length > 0) await AlertAsync(record, plan, alerts, cancellationToken);
            await live.PublishAsync(new LiveEvent("launch.pacing_changed", new { campaignId = record.RoomId }, LiveAudience.Everyone), cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch { await database.RollbackAsync(CancellationToken.None); throw; }
        return true;
    }

    private async Task<HashSet<DateOnly>> RecordedDatesAsync(Guid id, PacingWindow window, CancellationToken cancellationToken) =>
        [.. await db.Pacing.Where(row => row.LaunchId == id && row.Date >= window.First && row.Date <= window.Last).Select(row => row.Date).Distinct().ToListAsync(cancellationToken)];

    private static IEnumerable<DateOnly> Dates(PacingWindow window)
    {
        for (var date = window.First; date <= window.Last; date = date.AddDays(1)) yield return date;
    }

    private async Task AlertAsync(LaunchRecord record, LaunchPlan plan, PacingDay[] alerts, CancellationToken cancellationToken)
    {
        var brand = (await brands.GetAsync([record.BrandId], cancellationToken)).Single();
        var owners = await approvers.ForAsync(record.BrandId, ApprovalDuty.BudgetOwner, cancellationToken);
        var recipients = (await members.ListAsync(cancellationToken))
            .Where(member => member.Roles.HasFlag(BrandRoles.Marketer) || (owners.Count > 0 ? owners.Any(owner => owner.UserId == member.UserId) : member.Roles.HasFlag(BrandRoles.Approver)))
            .Select(member => member.UserId).Distinct().ToArray();
        var lines = alerts.OrderBy(row => row.CampaignType, StringComparer.Ordinal).Select(row =>
            $"{CampaignName(row.CampaignType)} spent {decimal.Round(Math.Abs(row.Deviation) * 100, 0).ToString(CultureInfo.InvariantCulture)}% {(row.Status == PacingStates.Over ? "over" : "under")} plan for {PacingRules.StreakDays} days in a row. {row.Cause} {row.SuggestedFix}");
        var date = alerts.Max(row => row.Date);
        if (recipients.Length > 0)
            await notifications.NotifyAsync(new Notification("pacing.alert", LiveAudience.Users(recipients), $"{plan.Title} is off its spending plan",
                string.Join(" ", lines), $"/{brand.Handle}/campaigns/{record.RoomId}/performance",
                $"launch:{record.RoomId:N}:pacing:{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"), cancellationToken);
        await audit.RecordAsync(new AuditRecord("launch.pacing_alerted", "campaign_room", record.RoomId.ToString(), null,
            new { date, campaigns = alerts.Select(row => new { row.CampaignType, row.Status, row.Spend, row.PlannedSpend, row.Deviation, row.CauseKind, row.Cause }) },
            LaunchRunner.Actor), cancellationToken);
    }

    private async Task<Dictionary<(string Campaign, DateOnly Date), CampaignDay>> MetricsAsync(string account, IEnumerable<string> campaigns, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var rows = await google.SearchAsync(account, $"SELECT campaign.resource_name, segments.date, metrics.cost_micros, metrics.conversions, metrics.conversions_value, metrics.impressions, metrics.clicks FROM campaign WHERE campaign.resource_name IN ({Filter(campaigns)}) AND segments.date BETWEEN '{Iso(first)}' AND '{Iso(last)}'", cancellationToken);
        var days = new Dictionary<(string, DateOnly), CampaignDay>();
        foreach (var row in rows)
        {
            if (!DateOnly.TryParseExact(LaunchPreflight.Text(row.GetProperty("segments"), "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var metrics = row.TryGetProperty("metrics", out var values) ? values : default;
            var key = (LaunchPreflight.Text(row.GetProperty("campaign"), "resourceName"), date);
            var day = new CampaignDay(Number(metrics, "costMicros") / 1_000_000m, Number(metrics, "conversions"), Number(metrics, "conversionsValue"),
                (long)Number(metrics, "impressions"), (long)Number(metrics, "clicks"));
            days[key] = days.TryGetValue(key, out var earlier)
                ? new(earlier.Spend + day.Spend, earlier.Conversions + day.Conversions, earlier.ConversionValue + day.ConversionValue, earlier.Impressions + day.Impressions, earlier.Clicks + day.Clicks)
                : day;
        }
        return days;
    }

    private async Task<Dictionary<string, PacingSignals>> SignalsAsync(Guid launchId, string account, Dictionary<string, string> campaigns, PacingDay[] alerts, PacingDay[] days, CancellationToken cancellationToken)
    {
        var names = alerts.Select(row => campaigns[row.CampaignType]).ToArray();
        var statuses = await google.SearchAsync(account, $"SELECT campaign.resource_name, campaign.primary_status, campaign.primary_status_reasons FROM campaign WHERE campaign.resource_name IN ({Filter(names)})", cancellationToken);
        var ads = await google.SearchAsync(account, $"SELECT campaign.resource_name, ad_group_ad.resource_name, ad_group_ad.status, ad_group_ad.policy_summary.approval_status FROM ad_group_ad WHERE campaign.resource_name IN ({Filter(names)}) AND ad_group_ad.status != 'REMOVED'", cancellationToken);
        var searchable = alerts.Where(row => row.CampaignType is "search" or "shopping").ToArray();
        var ranks = searchable.Length == 0 ? [] : await google.SearchAsync(account,
            $"SELECT campaign.resource_name, metrics.search_rank_lost_impression_share FROM campaign WHERE campaign.resource_name IN ({Filter(searchable.Select(row => campaigns[row.CampaignType]))}) AND segments.date BETWEEN '{Iso(searchable.Min(row => row.Date).AddDays(1 - PacingRules.StreakDays))}' AND '{Iso(searchable.Max(row => row.Date))}'", cancellationToken);
        var stockPaused = await db.AdPauses.AsNoTracking().Where(pause => pause.LaunchId == launchId && pause.State == PauseStates.Paused)
            .GroupBy(pause => pause.CampaignType).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
        var signals = new Dictionary<string, PacingSignals>();
        foreach (var row in alerts)
        {
            var name = campaigns[row.CampaignType];
            var status = statuses.FirstOrDefault(item => Campaign(item) == name);
            var reasons = status.ValueKind == JsonValueKind.Object && status.GetProperty("campaign").TryGetProperty("primaryStatusReasons", out var list)
                ? list.EnumerateArray().Select(reason => reason.GetString() ?? "").ToArray() : [];
            var campaignAds = ads.Where(item => Campaign(item) == name).Select(item => item.GetProperty("adGroupAd")).ToArray();
            var blocked = campaignAds.Count(ad => LaunchPreflight.Text(ad, "status") == "PAUSED"
                || ad.TryGetProperty("policySummary", out var policy) && LaunchPreflight.Text(policy, "approvalStatus") == "DISAPPROVED");
            var rank = ranks.Where(item => Campaign(item) == name && item.TryGetProperty("metrics", out var metrics) && metrics.TryGetProperty("searchRankLostImpressionShare", out _))
                .Select(item => (decimal?)Number(item.GetProperty("metrics"), "searchRankLostImpressionShare")).FirstOrDefault();
            var streakStart = row.Date.AddDays(1 - PacingRules.StreakDays);
            var campaignDays = days.Where(day => day.CampaignType == row.CampaignType).ToArray();
            signals[row.CampaignType] = new PacingSignals(reasons, Math.Max(campaignAds.Length, stockPaused.GetValueOrDefault(row.CampaignType)), Math.Max(blocked, stockPaused.GetValueOrDefault(row.CampaignType)), rank,
                Cpc(campaignDays.Where(day => day.Date >= streakStart && day.Date <= row.Date)), Cpc(campaignDays.Where(day => day.Date < streakStart)), stockPaused.GetValueOrDefault(row.CampaignType));
        }
        return signals;
    }

    private static decimal? Cpc(IEnumerable<PacingDay> days)
    {
        var list = days.ToArray();
        var clicks = list.Sum(day => day.Clicks);
        return clicks == 0 ? null : list.Sum(day => day.Spend) / clicks;
    }

    private static string Campaign(JsonElement row) => row.TryGetProperty("campaign", out var campaign) ? LaunchPreflight.Text(campaign, "resourceName") : "";

    private static decimal Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }

    private static string Filter(IEnumerable<string> names) => string.Join(',', names.Select(name => "'" + name + "'"));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string CampaignName(string type) => type switch { "search" => "Search", "shopping" => "Shopping", "demand_gen" => "Demand Gen", _ => type };
}
