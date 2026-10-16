using Kairo.Contracts.Identity;
using Kairo.Contracts.Launch;
using Kairo.Modules.Care.Recommendations;

namespace Kairo.IntegrationTests.Care;

public sealed class RecommendationRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateOnly From = new(2026, 11, 1);

    private static CampaignState Campaign(string type, decimal cost, decimal conversions, decimal value = 0, decimal total = 10000, decimal spent = 4000,
        string strategy = "maximize_clicks", decimal? target = null, string status = "ENABLED", int daysLeft = 10) =>
        new(type, status, Today.AddDays(daysLeft - 1), total, spent, strategy, target, cost, conversions, value);

    private static IReadOnlyList<RecommendationDraft> Draft(string kpi, params CampaignState[] campaigns) =>
        RecommendationRules.Draft(kpi, "INR", campaigns, From, Today, Today);

    [Fact]
    public void A_quarter_of_the_weaker_campaigns_remaining_budget_moves_to_the_cheaper_one()
    {
        var move = Assert.Single(Draft("orders", Campaign("search", 1000, 50), Campaign("shopping", 2000, 40)));

        Assert.Equal(RecommendationTypes.BudgetMove, move.Type);
        Assert.Equal(("shopping", "search", 1500m), (move.Change.From, move.Change.To, move.Change.Amount));
        Assert.Equal([new BudgetSetting("shopping", 8500), new BudgetSetting("search", 11500)], move.Change.Budgets!);
        Assert.Equal("Move INR 1,500 from Shopping to Search", move.Title);
        Assert.StartsWith("About 45 more orders", move.ExpectedEffect, StringComparison.Ordinal);
        Assert.Contains("INR 20 per order against INR 50", move.Reasoning, StringComparison.Ordinal);
    }

    [Fact]
    public void Revenue_tests_move_budget_toward_the_higher_return()
    {
        var move = Assert.Single(Draft("revenue", Campaign("search", 1000, 20, value: 2000), Campaign("demand_gen", 1000, 20, value: 5000)));

        Assert.Equal(("search", "demand_gen"), (move.Change.From, move.Change.To));
        Assert.StartsWith("About INR 4,500 more revenue", move.ExpectedEffect, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1000, 50, 1200, 50)]
    [InlineData(1000, 14, 2000, 40)]
    public void Close_results_or_thin_data_move_nothing(decimal searchCost, decimal searchConversions, decimal shoppingCost, decimal shoppingConversions) =>
        Assert.Empty(Draft("orders", Campaign("search", searchCost, searchConversions), Campaign("shopping", shoppingCost, shoppingConversions)));

    [Fact]
    public void Paused_ended_or_spent_campaigns_get_no_moves()
    {
        Assert.Empty(Draft("orders", Campaign("search", 1000, 50), Campaign("shopping", 2000, 40, status: "PAUSED")));
        Assert.Empty(Draft("orders", Campaign("search", 1000, 50), Campaign("shopping", 2000, 40, daysLeft: 1)));
        Assert.Empty(Draft("orders", Campaign("search", 1000, 50), Campaign("shopping", 2000, 40, spent: 10000)));
    }

    [Fact]
    public void Targets_more_than_a_fifth_away_from_the_result_move_to_what_was_achieved()
    {
        var cpa = Assert.Single(Draft("orders", Campaign("search", 1200, 60, strategy: "target_cpa", target: 10)));
        Assert.Equal((RecommendationTypes.TargetChange, "target_cpa", 10m, 20m), (cpa.Type, cpa.Change.BiddingStrategy, cpa.Change.CurrentTarget, cpa.Change.Target));
        Assert.Contains("more orders", cpa.ExpectedEffect, StringComparison.Ordinal);

        var roas = Assert.Single(Draft("revenue", Campaign("shopping", 1000, 30, value: 5000, strategy: "target_roas", target: 3)));
        Assert.Equal(5m, roas.Change.Target);
        Assert.Equal("Set the Shopping target return on ad spend to 500%", roas.Title);

        Assert.Empty(Draft("orders", Campaign("search", 1100, 60, strategy: "target_cpa", target: 20)));
    }

    [Fact]
    public void Drift_catches_budget_target_and_status_changes_and_overspent_budgets()
    {
        var campaigns = new[] { Campaign("search", 1000, 50), Campaign("shopping", 2000, 40) };
        var move = Draft("orders", campaigns).Single();

        Assert.Null(RecommendationRules.Drift(move.Change, move.Baseline, campaigns, Today));
        Assert.NotNull(RecommendationRules.Drift(move.Change, move.Baseline, [campaigns[0] with { TotalBudget = 9000 }, campaigns[1]], Today));
        Assert.NotNull(RecommendationRules.Drift(move.Change, move.Baseline, [campaigns[0], campaigns[1] with { Status = "PAUSED" }], Today));
        Assert.NotNull(RecommendationRules.Drift(move.Change, move.Baseline, [campaigns[0], campaigns[1] with { SpentToDate = 9000 }], Today));
        Assert.NotNull(RecommendationRules.Drift(move.Change, move.Baseline, [campaigns[0]], Today));
    }

    [Fact]
    public void Held_hidden_and_budget_owner_rules_follow_the_test_and_spend()
    {
        Assert.True(RecommendationRules.Held(RecommendationSources.Kairo, testRunning: true));
        Assert.False(RecommendationRules.Held(RecommendationSources.Google, testRunning: true));
        Assert.True(RecommendationRules.Visible(RecommendationSources.Google, "KEYWORD", testRunning: true));
        Assert.False(RecommendationRules.Visible(RecommendationSources.Google, "DISPLAY_EXPANSION_OPT_IN", testRunning: true));
        Assert.False(RecommendationRules.Visible(RecommendationSources.Google, "SOMETHING_NEW", testRunning: true));
        Assert.True(RecommendationRules.Visible(RecommendationSources.Google, "SOMETHING_NEW", testRunning: false));
        Assert.True(RecommendationRules.NeedsBudgetOwner(RecommendationSources.Google, "CAMPAIGN_BUDGET"));
        Assert.False(RecommendationRules.NeedsBudgetOwner(RecommendationSources.Kairo, RecommendationTypes.BudgetMove));
        Assert.Equal(ApprovalDuty.BudgetMoves, RecommendationRules.DutyOf(RecommendationSources.Kairo, RecommendationTypes.BudgetMove));
        Assert.Equal(ApprovalDuty.TargetChanges, RecommendationRules.DutyOf(RecommendationSources.Kairo, RecommendationTypes.TargetChange));
        Assert.Equal(ApprovalDuty.GoogleSuggestions, RecommendationRules.DutyOf(RecommendationSources.Google, "KEYWORD"));
    }
}
