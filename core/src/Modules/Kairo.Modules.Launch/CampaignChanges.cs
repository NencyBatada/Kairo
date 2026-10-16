using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;

namespace Kairo.Modules.Launch;

internal sealed class CampaignChanges(LaunchDbContext db, LaunchGoogle google, TimeProvider time) : ICampaignChanges
{
    private sealed record LiveCampaigns(LaunchPlan Plan, string Account, Dictionary<string, string> Resources)
    {
        public string Campaign(string type) => Resources.TryGetValue("campaign:" + type, out var name) ? name : throw Unknown();

        public string Budget(string type) => Resources.TryGetValue("budget:" + type, out var name) ? name : throw Unknown();

        public string Filter => string.Join(',', Plan.Settings.Channels.Select(channel => "'" + Campaign(channel.CampaignType) + "'"));

        public string? TypeOf(string campaign) => Plan.Settings.Channels.Select(channel => channel.CampaignType).FirstOrDefault(type => Resources.GetValueOrDefault("campaign:" + type) == campaign);

        private static CampaignChangeException Unknown() => new("This campaign type isn't part of the launched campaign.", "Refresh the recommendations and try again.");
    }

    public Task<IReadOnlyList<CampaignState>> ReadAsync(Guid roomId, DateOnly from, DateOnly to, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var live = await LiveAsync(roomId, cancellationToken);
        var today = Today(live.Plan);
        var start = from < live.Plan.Start ? from : live.Plan.Start;
        var settings = await google.SearchAsync(live.Account, $"SELECT campaign.resource_name, campaign.status, campaign.bidding_strategy_type, campaign.target_cpa.target_cpa_micros, campaign.target_roas.target_roas, campaign_budget.resource_name, campaign_budget.total_amount_micros FROM campaign WHERE campaign.resource_name IN ({live.Filter})", cancellationToken);
        var days = await google.SearchAsync(live.Account, $"SELECT campaign.resource_name, segments.date, metrics.cost_micros, metrics.conversions, metrics.conversions_value FROM campaign WHERE campaign.resource_name IN ({live.Filter}) AND segments.date BETWEEN '{Iso(start)}' AND '{Iso(today)}'", cancellationToken);
        var states = new List<CampaignState>();
        foreach (var channel in live.Plan.Settings.Channels)
        {
            var name = live.Campaign(channel.CampaignType);
            var row = settings.SingleOrDefault(row => LaunchPreflight.Text(row.GetProperty("campaign"), "resourceName") == name);
            if (row.ValueKind != JsonValueKind.Object) throw new CampaignChangeException("Google returned no settings for a launched campaign.", "Check that the campaign still exists in Google Ads.");
            var campaign = row.GetProperty("campaign");
            var strategy = Strategy(LaunchPreflight.Text(campaign, "biddingStrategyType"));
            decimal? target = strategy switch
            {
                "target_cpa" when campaign.TryGetProperty("targetCpa", out var cpa) => Micros(cpa, "targetCpaMicros"),
                "target_roas" when campaign.TryGetProperty("targetRoas", out var roas) => Number(roas, "targetRoas"),
                _ => null,
            };
            var mine = days.Where(day => LaunchPreflight.Text(day.GetProperty("campaign"), "resourceName") == name)
                .Select(day => (Date: DateOnly.ParseExact(LaunchPreflight.Text(day.GetProperty("segments"), "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture), Metrics: day.TryGetProperty("metrics", out var metrics) ? metrics : default))
                .ToArray();
            var window = mine.Where(day => day.Date >= from && day.Date <= to).ToArray();
            states.Add(new CampaignState(channel.CampaignType, LaunchPreflight.Text(campaign, "status"), live.Plan.End,
                row.TryGetProperty("campaignBudget", out var budget) ? Micros(budget, "totalAmountMicros") : 0m,
                mine.Sum(day => Micros(day.Metrics, "costMicros")), strategy, target,
                window.Sum(day => Micros(day.Metrics, "costMicros")), window.Sum(day => Number(day.Metrics, "conversions")), window.Sum(day => Number(day.Metrics, "conversionsValue"))));
        }

        return (IReadOnlyList<CampaignState>)states;
    });

    public Task<IReadOnlyList<GoogleRecommendation>> GoogleRecommendationsAsync(Guid roomId, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var live = await LiveAsync(roomId, cancellationToken);
        var rows = await google.SearchAsync(live.Account, $"SELECT recommendation.resource_name, recommendation.type, recommendation.campaign, recommendation.impact.base_metrics.cost_micros, recommendation.impact.base_metrics.conversions, recommendation.impact.base_metrics.conversions_value, recommendation.impact.base_metrics.clicks, recommendation.impact.potential_metrics.cost_micros, recommendation.impact.potential_metrics.conversions, recommendation.impact.potential_metrics.conversions_value, recommendation.impact.potential_metrics.clicks, recommendation.campaign_budget_recommendation.current_budget_amount_micros, recommendation.campaign_budget_recommendation.recommended_budget_amount_micros, recommendation.keyword_recommendation.keyword.text, recommendation.keyword_recommendation.keyword.match_type FROM recommendation WHERE recommendation.campaign IN ({live.Filter}) AND recommendation.dismissed = FALSE", cancellationToken);
        var recommendations = new List<GoogleRecommendation>();
        foreach (var row in rows)
        {
            var item = row.GetProperty("recommendation");
            var type = live.TypeOf(LaunchPreflight.Text(item, "campaign"));
            var name = LaunchPreflight.Text(item, "resourceName");
            if (type is null || !name.StartsWith($"customers/{live.Account}/recommendations/", StringComparison.Ordinal)) continue;
            var impact = item.TryGetProperty("impact", out var value) ? value : default;
            var before = impact.ValueKind == JsonValueKind.Object && impact.TryGetProperty("baseMetrics", out var baseMetrics) ? baseMetrics : default;
            var after = impact.ValueKind == JsonValueKind.Object && impact.TryGetProperty("potentialMetrics", out var potential) ? potential : default;
            recommendations.Add(new GoogleRecommendation(name, LaunchPreflight.Text(item, "type"), type,
                new GoogleImpact(Micros(before, "costMicros"), Micros(after, "costMicros"), Number(before, "conversions"), Number(after, "conversions"),
                    Number(before, "conversionsValue"), Number(after, "conversionsValue"), (long)Number(before, "clicks"), (long)Number(after, "clicks")),
                item.Clone()));
        }

        return (IReadOnlyList<GoogleRecommendation>)recommendations;
    });

    public Task SetBudgetsAsync(Guid roomId, IReadOnlyList<BudgetSetting> budgets, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var live = await LiveAsync(roomId, cancellationToken);
        var operations = budgets.Select(budget => (object)new
        {
            campaignBudgetOperation = new { update = new { resourceName = live.Budget(budget.CampaignType), totalAmountMicros = CampaignBuild.Micros(budget.Total) }, updateMask = "total_amount_micros" },
        }).ToArray();
        await MutateAsync(live, operations, cancellationToken);
        return true;
    });

    public Task SetTargetAsync(Guid roomId, string campaignType, string biddingStrategy, decimal target, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var live = await LiveAsync(roomId, cancellationToken);
        var resource = live.Campaign(campaignType);
        object update = biddingStrategy switch
        {
            "target_cpa" => new { campaignOperation = new { update = new { resourceName = resource, targetCpa = new { targetCpaMicros = CampaignBuild.Micros(target) } }, updateMask = "target_cpa.target_cpa_micros" } },
            "target_roas" => new { campaignOperation = new { update = new { resourceName = resource, targetRoas = new { targetRoas = target } }, updateMask = "target_roas.target_roas" } },
            _ => throw new CampaignChangeException("This campaign doesn't bid to a target.", "Refresh the recommendations and try again."),
        };
        await MutateAsync(live, [update], cancellationToken);
        return true;
    });

    public Task ApplyGoogleAsync(Guid roomId, string resourceName, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var live = await LiveAsync(roomId, cancellationToken);
        if (!resourceName.StartsWith($"customers/{live.Account}/recommendations/", StringComparison.Ordinal))
            throw new CampaignChangeException("This Google recommendation belongs to another account.", "Refresh the recommendations and try again.");
        using var response = await google.SendAsync(ConnectedProduct.GoogleAds, live.Account, HttpMethod.Post,
            $"https://googleads.googleapis.com/{google.Version}/customers/{live.Account}/recommendations:apply",
            JsonContent.Create(new { operations = new[] { new { resourceName } }, partialFailure = false }), true, cancellationToken);
        return true;
    });

    private async Task MutateAsync(LiveCampaigns live, object[] operations, CancellationToken cancellationToken)
    {
        await google.MutateAsync(live.Account, operations, true, cancellationToken);
        await google.MutateAsync(live.Account, operations, false, cancellationToken);
    }

    private async Task<LiveCampaigns> LiveAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var record = await db.Launches.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == roomId && row.Status == "live", cancellationToken)
            ?? throw new CampaignChangeException("This campaign isn't live in Google Ads.", "Launch the campaign before changing it.");
        var plan = LaunchRunner.Read<LaunchPlan>(record.Plan);
        return new LiveCampaigns(plan, plan.GoogleAdsAccountId!, LaunchRunner.Read<Dictionary<string, string>>(record.Resources));
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (LaunchException exception)
        {
            var message = exception.Code == "google_validation" ? exception.Message.Replace("this launch step", "this change", StringComparison.Ordinal) : exception.Message;
            throw new CampaignChangeException(message, exception.Fix, exception.Transient);
        }
        catch (ConnectionUnavailableException)
        {
            throw new CampaignChangeException("The Google Ads account needs reconnecting.", "Reconnect Google Ads, then try again.");
        }
    }

    private DateOnly Today(LaunchPlan plan) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(plan.TimeZone)).DateTime);

    private static string Strategy(string type) => type switch
    {
        "TARGET_CPA" => "target_cpa",
        "TARGET_ROAS" => "target_roas",
        "TARGET_SPEND" => "maximize_clicks",
        _ => type.ToLowerInvariant(),
    };

    private static decimal Micros(JsonElement value, string property) => Number(value, property) / 1_000_000m;

    private static decimal Number(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var number)
        && decimal.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
