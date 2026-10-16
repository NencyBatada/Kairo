using System.Text.Json;
using Kairo.Contracts.Launch;
using Kairo.Modules.CampaignRooms.LaunchPlans;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class LaunchPlanRulesTests
{
    public static LaunchPlan Valid() => new(Guid.NewGuid(), Guid.NewGuid(), "Rain shoes", 1, 1, 1000m, "INR", "Asia/Kolkata",
        new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 29), ["en"], [new("1007785", "Mumbai")], [new("1007788", "Pune")],
        "123", "456", null, ["p1"], ["https://example.com/shoes"], new LaunchSettings([new("search", 1000m, "maximize_clicks")]),
        [new(Guid.NewGuid(), 1, "search_ad", "search", "en", "Shoes", null, ["p1"], JsonSerializer.SerializeToElement(new { headlines = new[] { "Shoes" } }),
            "passed", JsonSerializer.SerializeToElement(new { }), null, null, 0, false, null)]);

    [Fact]
    public void Complete_plan_passes_and_spend_settings_are_bound_to_its_snapshot()
    {
        var plan = Valid();
        Assert.Null(LaunchPlanDesk.Validate(plan));
        Assert.NotEqual(LaunchPlanDesk.Serialize(plan), LaunchPlanDesk.Serialize(plan with { Settings = new LaunchSettings([new("search", 1000, "target_cpa", 20)]) }));
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    [InlineData(0)]
    public void Allocations_must_match_the_brief(decimal amount) =>
        Assert.NotNull(LaunchPlanDesk.Validate(Valid() with { Settings = new LaunchSettings([new("search", amount, "maximize_clicks")]) }));

    [Fact]
    public void Missing_connections_regions_and_checked_assets_block_launch()
    {
        var plan = Valid();
        Assert.NotNull(LaunchPlanDesk.Validate(plan with { GoogleAdsAccountId = null }));
        Assert.NotNull(LaunchPlanDesk.Validate(plan with { Regions = [new("unresolved", "Mumbai")] }));
        Assert.NotNull(LaunchPlanDesk.Validate(plan with { Holdouts = plan.Regions }));
        Assert.NotNull(LaunchPlanDesk.Validate(plan with { Assets = [] }));
        Assert.NotNull(LaunchPlanDesk.Validate(plan with { Assets = [plan.Assets[0] with { CheckState = "failed" }] }));
    }

    [Theory]
    [InlineData("target_cpa", null)]
    [InlineData("target_roas", -1)]
    [InlineData("unknown", 1)]
    [InlineData("maximize_clicks", 2)]
    public void Unsupported_or_incomplete_bidding_is_rejected(string strategy, int? target) =>
        Assert.NotNull(LaunchPlanDesk.Validate(Valid() with { Settings = new LaunchSettings([new("search", 1000, strategy, target)]) }));
}
