using System.Globalization;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Launch;

namespace Kairo.Modules.Care.Recommendations;

internal static class RecommendationStates
{
    public const string Proposed = "proposed";
    public const string Approved = "approved";
    public const string Applied = "applied";
    public const string Dismissed = "dismissed";
    public const string Expired = "expired";
}

internal static class RecommendationSources
{
    public const string Kairo = "kairo";
    public const string Google = "google";
}

internal static class RecommendationTypes
{
    public const string BudgetMove = "budget_move";
    public const string TargetChange = "target_change";
}

internal sealed record ChangeDetails(
    BudgetSetting[]? Budgets = null,
    string? From = null,
    string? To = null,
    decimal? Amount = null,
    string? CampaignType = null,
    string? BiddingStrategy = null,
    decimal? CurrentTarget = null,
    decimal? Target = null,
    string? GoogleResource = null);

internal sealed record CampaignBaseline(string CampaignType, decimal TotalBudget, string BiddingStrategy, decimal? Target);

internal sealed record RecommendationDraft(string Type, string CampaignType, string Title, string ExpectedEffect, string Reasoning,
    ChangeDetails Change, CampaignBaseline[] Baseline, object Evidence);

internal static class RecommendationRules
{
    public const int MinConversions = 15;
    public const int MinDaysLeft = 2;
    public const decimal BudgetGap = 1.25m;
    public const decimal MoveShare = 0.25m;
    public const decimal TargetGap = 0.20m;

    public static readonly HashSet<string> VisibleDuringTest = new(StringComparer.Ordinal)
    {
        "KEYWORD", "KEYWORD_MATCH_TYPE", "USE_BROAD_MATCH_KEYWORD", "TEXT_AD", "RESPONSIVE_SEARCH_AD", "RESPONSIVE_SEARCH_AD_ASSET",
        "RESPONSIVE_SEARCH_AD_IMPROVE_AD_STRENGTH", "IMPROVE_DEMAND_GEN_AD_STRENGTH", "OPTIMIZE_AD_ROTATION", "SITELINK_ASSET", "CALLOUT_ASSET",
        "CALL_ASSET", "LEAD_FORM_ASSET", "CAMPAIGN_BUDGET", "FORECASTING_CAMPAIGN_BUDGET", "MARGINAL_ROI_CAMPAIGN_BUDGET", "MOVE_UNUSED_BUDGET",
        "TARGET_CPA_OPT_IN", "TARGET_ROAS_OPT_IN", "MAXIMIZE_CONVERSIONS_OPT_IN", "MAXIMIZE_CONVERSION_VALUE_OPT_IN", "MAXIMIZE_CLICKS_OPT_IN",
        "SET_TARGET_CPA", "SET_TARGET_ROAS", "RAISE_TARGET_CPA", "LOWER_TARGET_ROAS", "RAISE_TARGET_CPA_BID_TOO_LOW",
    };

    public static readonly HashSet<string> RaisesSpend = new(StringComparer.Ordinal)
    {
        "CAMPAIGN_BUDGET", "FORECASTING_CAMPAIGN_BUDGET", "MARGINAL_ROI_CAMPAIGN_BUDGET",
    };

    public static ApprovalDuty DutyOf(string source, string type) => source == RecommendationSources.Google
        ? ApprovalDuty.GoogleSuggestions
        : type == RecommendationTypes.BudgetMove ? ApprovalDuty.BudgetMoves : ApprovalDuty.TargetChanges;

    public static string DutyKey(ApprovalDuty duty) => duty switch
    {
        ApprovalDuty.BudgetMoves => "budget_moves",
        ApprovalDuty.TargetChanges => "target_changes",
        _ => "google_suggestions",
    };

    public static bool Visible(string source, string type, bool testRunning) =>
        source == RecommendationSources.Kairo || !testRunning || VisibleDuringTest.Contains(type);

    public static bool Held(string source, bool testRunning) => source == RecommendationSources.Kairo && testRunning;

    public static bool NeedsBudgetOwner(string source, string type) => source == RecommendationSources.Google && RaisesSpend.Contains(type);

    public static IReadOnlyList<RecommendationDraft> Draft(string kpi, string currency, IReadOnlyList<CampaignState> campaigns, DateOnly from, DateOnly to, DateOnly today)
    {
        var evidence = new
        {
            kpi,
            window = new { from, to },
            campaigns = campaigns.Select(campaign => new
            {
                campaign.CampaignType, campaign.Status, campaign.Cost, campaign.Conversions, campaign.ConversionValue,
                campaign.TotalBudget, campaign.SpentToDate, campaign.BiddingStrategy, campaign.Target,
            }).ToArray(),
        };
        var baseline = campaigns.Select(campaign => new CampaignBaseline(campaign.CampaignType, campaign.TotalBudget, campaign.BiddingStrategy, campaign.Target)).ToArray();
        var eligible = campaigns.Where(campaign => Eligible(campaign, today)).ToList();
        var drafts = new List<RecommendationDraft>();
        if (BudgetMove(kpi, currency, eligible, evidence, baseline) is { } move) drafts.Add(move);
        drafts.AddRange(eligible.Select(campaign => TargetChange(kpi, currency, campaign, evidence, baseline)).OfType<RecommendationDraft>());
        return drafts;
    }

    public static string? Drift(ChangeDetails change, CampaignBaseline[] baseline, IReadOnlyList<CampaignState> current, DateOnly today)
    {
        foreach (var expected in baseline.Where(item => Involves(change, item.CampaignType)))
        {
            var campaign = current.FirstOrDefault(state => state.CampaignType == expected.CampaignType);
            if (campaign is null || campaign.Status != "ENABLED" || campaign.End < today) return "A campaign in this recommendation has stopped or ended.";
            if (campaign.TotalBudget != expected.TotalBudget || campaign.BiddingStrategy != expected.BiddingStrategy || campaign.Target != expected.Target)
                return "The campaign changed after Kairo made this recommendation.";
        }

        if (change.Budgets is { } budgets)
        {
            foreach (var budget in budgets)
                if (current.FirstOrDefault(state => state.CampaignType == budget.CampaignType) is { } campaign && campaign.SpentToDate > budget.Total)
                    return "The campaign has already spent more than its new budget.";
        }

        return null;
    }

    public static string Label(string campaignType) => campaignType switch
    {
        "search" => "Search",
        "shopping" => "Shopping",
        "demand_gen" => "Demand Gen",
        _ => campaignType,
    };

    public static string Money(string currency, decimal amount, int decimals = 0) =>
        currency + " " + amount.ToString(decimals == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);

    private static bool Involves(ChangeDetails change, string campaignType) =>
        change.Budgets?.Any(budget => budget.CampaignType == campaignType) == true || change.CampaignType == campaignType;

    private static bool Eligible(CampaignState campaign, DateOnly today) =>
        campaign.Status == "ENABLED" && campaign.End.DayNumber - today.DayNumber + 1 >= MinDaysLeft && campaign.Conversions >= MinConversions && campaign.Cost > 0;

    private static RecommendationDraft? BudgetMove(string kpi, string currency, List<CampaignState> eligible, object evidence, CampaignBaseline[] baseline)
    {
        if (eligible.Count < 2) return null;
        var revenue = kpi == "revenue";
        var ranked = (revenue
            ? eligible.Where(campaign => campaign.ConversionValue > 0).OrderByDescending(campaign => campaign.ConversionValue / campaign.Cost)
            : eligible.OrderBy(campaign => campaign.Cost / campaign.Conversions)).ToList();
        if (ranked.Count < 2) return null;
        var (best, worst) = (ranked[0], ranked[^1]);
        var ratio = revenue ? (best.ConversionValue / best.Cost) / (worst.ConversionValue / worst.Cost) : (worst.Cost / worst.Conversions) / (best.Cost / best.Conversions);
        var amount = decimal.Floor((worst.TotalBudget - worst.SpentToDate) * MoveShare);
        if (ratio < BudgetGap || amount < 1) return null;
        var (singular, plural) = Result(kpi);
        string effect, reasoning;
        if (revenue)
        {
            var (bestRoas, worstRoas) = (best.ConversionValue / best.Cost, worst.ConversionValue / worst.Cost);
            effect = $"About {Money(currency, amount * (bestRoas - worstRoas))} more revenue for the same total spend, if each campaign keeps the return it had in the test.";
            reasoning = $"During the test, {Label(best.CampaignType)} returned {Money(currency, bestRoas, 2)} for every {Money(currency, 1)} spent, against {Money(currency, worstRoas, 2)} for {Label(worst.CampaignType)}. Moving a quarter of {Label(worst.CampaignType)}'s remaining budget puts more money where it earned more.";
        }
        else
        {
            var (bestCost, worstCost) = (best.Cost / best.Conversions, worst.Cost / worst.Conversions);
            var extra = amount / bestCost - amount / worstCost;
            effect = $"About {extra.ToString("N0", CultureInfo.InvariantCulture)} more {plural} for the same total spend, if each campaign keeps the cost per {singular} it had in the test.";
            reasoning = $"During the test, {Label(best.CampaignType)} cost {Money(currency, bestCost)} per {singular} against {Money(currency, worstCost)} for {Label(worst.CampaignType)}. Moving a quarter of {Label(worst.CampaignType)}'s remaining budget puts more money where {plural} came cheaper.";
        }

        var change = new ChangeDetails(
            Budgets: [new BudgetSetting(worst.CampaignType, worst.TotalBudget - amount), new BudgetSetting(best.CampaignType, best.TotalBudget + amount)],
            From: worst.CampaignType, To: best.CampaignType, Amount: amount);
        return new RecommendationDraft(RecommendationTypes.BudgetMove, best.CampaignType,
            $"Move {Money(currency, amount)} from {Label(worst.CampaignType)} to {Label(best.CampaignType)}", effect, reasoning, change, baseline, evidence);
    }

    private static RecommendationDraft? TargetChange(string kpi, string currency, CampaignState campaign, object evidence, CampaignBaseline[] baseline)
    {
        if (campaign.Target is not { } target || target <= 0) return null;
        var (singular, plural) = Result(kpi);
        var label = Label(campaign.CampaignType);
        if (campaign.BiddingStrategy == "target_cpa")
        {
            var achieved = decimal.Round(campaign.Cost / campaign.Conversions, 2);
            if (Math.Abs(achieved - target) / target <= TargetGap || achieved <= 0) return null;
            var effect = achieved < target
                ? $"Google bids toward the lower cost per {singular} the test achieved, which can cut costs but may bring fewer {plural}."
                : $"Google can bid for more {plural}, at about the cost per {singular} the test achieved.";
            return new RecommendationDraft(RecommendationTypes.TargetChange, campaign.CampaignType, $"Set the {label} target cost per {singular} to {Money(currency, achieved, 2)}", effect,
                $"{label} aims for {Money(currency, target, 2)} per {singular}, but the test achieved {Money(currency, achieved, 2)} across {campaign.Conversions.ToString("N0", CultureInfo.InvariantCulture)} {plural}.",
                new ChangeDetails(CampaignType: campaign.CampaignType, BiddingStrategy: campaign.BiddingStrategy, CurrentTarget: target, Target: achieved), baseline, evidence);
        }

        if (campaign.BiddingStrategy == "target_roas")
        {
            var achieved = decimal.Round(campaign.ConversionValue / campaign.Cost, 2);
            if (Math.Abs(achieved - target) / target <= TargetGap || achieved <= 0) return null;
            var effect = achieved > target
                ? "Google bids toward the higher return the test achieved, which can raise return on spend but may bring fewer sales."
                : "Google can bid for more sales, at about the return the test achieved.";
            return new RecommendationDraft(RecommendationTypes.TargetChange, campaign.CampaignType, $"Set the {label} target return on ad spend to {Percent(achieved)}", effect,
                $"{label} aims for a {Percent(target)} return on ad spend, but the test achieved {Percent(achieved)}.",
                new ChangeDetails(CampaignType: campaign.CampaignType, BiddingStrategy: campaign.BiddingStrategy, CurrentTarget: target, Target: achieved), baseline, evidence);
        }

        return null;
    }

    private static string Percent(decimal ratio) => (ratio * 100).ToString("N0", CultureInfo.InvariantCulture) + "%";

    private static (string Singular, string Plural) Result(string kpi) => kpi switch
    {
        "orders" => ("order", "orders"),
        "revenue" => ("sale", "sales"),
        _ => ("conversion", "conversions"),
    };
}
