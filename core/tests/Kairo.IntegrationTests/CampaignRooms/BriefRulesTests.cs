using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class BriefRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7.4, 7)]
    [InlineData(2007.4, 2000)]
    [InlineData(6050, 6100)]
    [InlineData(186543, 190000)]
    public void Budgets_round_to_two_significant_figures(double amount, double expected) =>
        Assert.Equal((decimal)expected, BudgetGuide.Nice((decimal)amount));

    [Fact]
    public void The_keyword_guide_scales_with_the_chosen_regions_share_of_searches()
    {
        var evidence = Evidence(bids: new EvidenceBids(10, 30), searches: 6000);

        var all = BudgetGuide.Estimate(evidence, ["Maharashtra", "Kerala"], 10, [], "INR");
        var one = BudgetGuide.Estimate(evidence, ["Kerala"], 10, [], "INR");

        Assert.Equal((1000m, 3000m, 2000m), (all.Low, all.High, all.Suggested));
        Assert.Equal((250m, 750m, 500m), (one.Low, one.High, one.Suggested));
        Assert.Contains(one.Evidence, item => item.Label == "Share in the chosen regions" && item.Value == "25%");
        Assert.Equal(750m, one.Clamp(5000m));
        Assert.Equal(250m, one.Clamp(1m));
    }

    [Fact]
    public void Without_bids_the_guide_falls_back_to_product_prices()
    {
        var guide = BudgetGuide.Estimate(Evidence(bids: null, searches: 6000), ["Maharashtra"], 14, [1000m, 2000m], "INR");

        Assert.Equal((7800m, 20000m, 13000m), (guide.Low, guide.High, guide.Suggested));
        Assert.StartsWith("No keyword bids", guide.Basis);
        Assert.Contains(guide.Evidence, item => item.Label == "Average product price" && item.Value == "₹1,500");
    }

    [Fact]
    public void Indian_rupees_use_lakh_grouping()
    {
        Assert.Equal("₹2,00,000", BudgetGuide.Money(200000m, "INR"));
        Assert.Equal("₹12.29", BudgetGuide.Money(12.29m, "INR"));
        Assert.Equal("USD 1,500", BudgetGuide.Money(1500m, "USD"));
    }

    [Fact]
    public void A_complete_brief_within_the_limit_has_no_warnings()
    {
        Assert.Empty(BriefChecks.Run(Complete(), Situation()));
    }

    [Fact]
    public void Missing_fields_and_bad_dates_block_approval()
    {
        var content = new BriefContent { StartDate = Today.AddDays(-1), EndDate = Today.AddDays(-3) };

        var warnings = BriefChecks.Run(content, Situation());

        Assert.All(warnings, warning => Assert.Equal(BriefSeverities.Blocking, warning.Severity));
        Assert.Equal(
            ["audiences", "budget", "campaignTypes", "dates", "dates", "keyMessages", "kpi", "languages", "objective", "products", "regions", "title"],
            warnings.Select(warning => warning.Field).Order());
        Assert.Contains(warnings, warning => warning.Code == "start_in_past");
        Assert.Contains(warnings, warning => warning.Code == "end_before_start");
    }

    [Fact]
    public void Budget_limit_moment_stock_and_connections_are_flagged()
    {
        var content = Complete();
        content.Budget = new BriefBudget { Amount = 300000, Currency = "INR" };
        content.EndDate = Today.AddDays(120);
        content.CampaignTypes = ["search", "shopping", "demand_gen"];
        content.ProductIds = ["a", "b"];

        var warnings = BriefChecks.Run(content, Situation(latestEnd: Today.AddDays(5), inStock: ["a"], connected: false));

        Assert.Equal(
            [
                ("over_budget_limit", "info"),
                ("too_long", "warning"),
                ("ends_after_moment", "warning"),
                ("out_of_stock", "warning"),
                ("merchant_center_not_linked", "warning"),
                ("youtube_not_connected", "warning"),
            ],
            warnings.Select(warning => (warning.Code, warning.Severity)));
        Assert.Equal("The budget is above the ₹2,00,000 limit, so a budget owner must approve it.", warnings[0].Message);
        Assert.Equal("Out of stock: Rain Boots.", warnings[3].Message);
    }

    [Fact]
    public void A_budget_in_another_currency_than_the_limit_goes_to_the_budget_owner()
    {
        var content = Complete();
        content.Budget = new BriefBudget { Amount = 10, Currency = "USD" };

        var warning = Assert.Single(BriefChecks.Run(content, Situation()));

        Assert.Equal(("budget_currency", "warning"), (warning.Code, warning.Severity));
    }

    [Fact]
    public void Only_the_fields_that_differ_count_as_changed()
    {
        var before = Complete();
        var after = before.Copy();
        after.Languages = ["en", "hi"];
        after.EndDate = Today.AddDays(20);
        after.SetReason(BriefFields.Languages, "Hindi.");

        Assert.Equal([BriefFields.Dates, BriefFields.Languages], BriefEditor.Changed(before, after));
        Assert.Empty(BriefEditor.Changed(before, before.Copy()));
        Assert.Empty(before.Reasons);
    }

    [Fact]
    public void Older_starter_briefs_read_as_smart_briefs()
    {
        var legacy = new BriefContent { Title = "Rain", Goal = "Sell", Audience = "Commuters", KeyMessage = "Stay dry", Channels = ["search"], Timing = "This week" };

        var normalized = legacy.Normalized();

        Assert.Equal(("Sell", "This week"), (normalized.Objective, normalized.Timing));
        Assert.Equal(["Commuters"], normalized.Audiences);
        Assert.Equal(["Stay dry"], normalized.KeyMessages);
        Assert.Equal(["search"], normalized.CampaignTypes);
        Assert.False(normalized.IsSmart);
    }

    [Theory]
    [InlineData(null, 1, 14)]
    [InlineData(5, 1, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(-3, 0, 0)]
    [InlineData(400, 1, 90)]
    public void Suggested_dates_follow_the_moment_and_stay_within_ninety_days(int? endsInDays, int startIn, int endIn)
    {
        var context = new BriefDeskContext(
            new Kairo.Contracts.Brands.BrandSummary(Guid.NewGuid(), "acme", "Acme", "INR", "Asia/Kolkata"),
            RoomEvidence.Empty,
            Today,
            endsInDays is { } days ? Today.AddDays(days) : null,
            [],
            [],
            new Kairo.Contracts.Identity.BudgetCap(null, "INR"),
            true,
            true);

        var (start, end) = BriefWork.SuggestedDates(context);

        Assert.Equal((Today.AddDays(startIn), Today.AddDays(endIn)), (start, end));
    }

    [Fact]
    public void The_test_scope_follows_the_briefs_regions_and_dates()
    {
        var brief = new Brief { Content = Complete() };
        brief.Content.Regions = ["Maharashtra", " "];
        brief.Content.StartDate = Today.AddDays(10);
        brief.Content.EndDate = Today.AddDays(30);

        var scope = BriefScope.Of(brief, RoomEvidence.Empty, TimeZoneInfo.Utc, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(["Maharashtra"], scope.Regions);
        Assert.Equal((Today.AddDays(10), Today.AddDays(30), 21), (scope.Start, scope.End, scope.Days));
        Assert.Equal(Today.AddDays(-1), scope.HistoryEnd);
    }

    [Fact]
    public void An_older_brief_without_dates_is_tested_from_tomorrow_until_the_moment_ends()
    {
        var brief = new Brief { Content = new BriefContent { Title = "Rain", Goal = "Sell", Regions = ["Mumbai"] } };
        var evidence = RoomEvidence.Empty with { Trigger = new EvidenceTrigger("weather", "Rain", [], new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)) };

        var scope = BriefScope.Of(brief, evidence, TimeZoneInfo.Utc, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal((Today.AddDays(1), new DateOnly(2026, 10, 19), Today.AddDays(-1)), (scope.Start, scope.End, scope.HistoryEnd));
    }

    [Fact]
    public void Only_region_and_date_changes_redesign_the_test()
    {
        Assert.True(BriefEditor.RedesignsTest([BriefFields.Budget, BriefFields.Dates]));
        Assert.True(BriefEditor.RedesignsTest([BriefFields.Regions]));
        Assert.False(BriefEditor.RedesignsTest([BriefFields.Budget, BriefFields.Languages, BriefFields.KeyMessages]));
    }

    private static RoomEvidence Evidence(EvidenceBids? bids, long searches) =>
        RoomEvidence.Empty with
        {
            Regions = [new EvidenceRegion("1", "Maharashtra", 300), new EvidenceRegion("2", "Kerala", 100)],
            LastMonthSearches = searches,
            Bids = bids,
        };

    private static BriefContent Complete() => new()
    {
        Title = "Monsoon runners",
        Objective = "Sell waterproof shoes.",
        Kpi = "orders",
        CampaignTypes = ["search"],
        Budget = new BriefBudget { Amount = 100000, Currency = "INR" },
        StartDate = Today.AddDays(1),
        EndDate = Today.AddDays(14),
        Regions = ["Maharashtra"],
        Audiences = ["Commuters"],
        KeyMessages = ["Stay dry."],
        ProductIds = ["a"],
        Languages = ["en"],
    };

    private static BriefSituation Situation(DateOnly? latestEnd = null, string[]? inStock = null, bool connected = true) => new(
        Today,
        latestEnd,
        200000m,
        "INR",
        new Dictionary<string, string> { ["a"] = "Runner", ["b"] = "Rain Boots" },
        (inStock ?? ["a", "b"]).ToHashSet(),
        connected,
        connected);
}
