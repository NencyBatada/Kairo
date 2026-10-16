using System.Text.Json;
using Kairo.Contracts.Launch;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.Modules.Launch.Google;

namespace Kairo.IntegrationTests.Launch;

public sealed class CampaignMutationTests
{
    [Fact]
    public void Approved_totals_are_encoded_as_fixed_budgets_and_every_campaign_starts_paused()
    {
        var plan = LaunchPlanRulesTests.Valid();
        var operations = Build(plan);
        var budget = Assert.Single(Creates(operations, "campaignBudgetOperation"));
        Assert.Equal("1000000000", budget.GetProperty("totalAmountMicros").GetString());
        Assert.Equal("CUSTOM", budget.GetProperty("period").GetString());
        Assert.False(budget.TryGetProperty("amountMicros", out _));
        Assert.Equal("PAUSED", Assert.Single(Creates(operations, "campaignOperation")).GetProperty("status").GetString());
    }

    [Fact]
    public void Holdouts_are_explicitly_excluded_and_location_targeting_requires_presence()
    {
        var operations = Build(LaunchPlanRulesTests.Valid());
        var criteria = Creates(operations, "campaignCriterionOperation");
        Assert.Contains(criteria, criterion => criterion.TryGetProperty("negative", out var value) && value.GetBoolean()
            && criterion.GetProperty("location").GetProperty("geoTargetConstant").GetString() == "geoTargetConstants/1007788");
        var campaign = Assert.Single(Creates(operations, "campaignOperation"));
        Assert.Equal("PRESENCE", campaign.GetProperty("geoTargetTypeSetting").GetProperty("positiveGeoTargetType").GetString());
        Assert.False(campaign.GetProperty("networkSettings").GetProperty("targetContentNetwork").GetBoolean());
    }

    [Fact]
    public void Shopping_targets_only_approved_offer_ids_and_excludes_everything_else()
    {
        var original = LaunchPlanRulesTests.Valid();
        var shopping = original.Assets[0] with { Kind = "shopping_title", CampaignType = "shopping", Content = JsonSerializer.SerializeToElement(new { offerId = "sku-1" }) };
        var plan = original with { Settings = new LaunchSettings([new("shopping", 1000m, "maximize_clicks")], SalesCountry: "IN"), Assets = [shopping] };
        var criteria = Creates(Build(plan), "adGroupCriterionOperation");
        Assert.Equal(3, criteria.Length);
        Assert.Contains(criteria, criterion => criterion.TryGetProperty("negative", out var negative) && negative.GetBoolean()
            && criterion.GetProperty("listingGroup").GetProperty("caseValue").GetProperty("productItemId").EnumerateObject().Count() == 0);
        Assert.Contains(criteria, criterion => criterion.GetProperty("listingGroup").TryGetProperty("caseValue", out var value)
            && value.GetProperty("productItemId").TryGetProperty("value", out var offer) && offer.GetString() == "sku-1");
    }

    [Fact]
    public void Money_conversion_uses_invariant_micros_and_rounds_half_away_from_zero() => Assert.Equal("1234568", CampaignBuild.Micros(1.2345675m));

    [Fact]
    public void Saved_upload_sessions_must_use_a_google_https_upload_host()
    {
        Assert.True(YouTubeUpload.AllowedSession("https://www.googleapis.com/upload-session/1"));
        Assert.False(YouTubeUpload.AllowedSession("https://attacker.example/upload"));
        Assert.False(YouTubeUpload.AllowedSession("http://www.googleapis.com/upload-session/1"));
    }

    private static JsonElement[] Build(LaunchPlan plan) => new CampaignBuild(plan, new Dictionary<string, string>(), new Dictionary<string, string> { ["en"] = "1000" }).Build()
        .Operations.Select(operation => JsonSerializer.SerializeToElement(operation)).ToArray();
    private static JsonElement[] Creates(IEnumerable<JsonElement> operations, string type) => operations.Where(operation => operation.TryGetProperty(type, out _))
        .Select(operation => operation.GetProperty(type).GetProperty("create")).ToArray();
}
