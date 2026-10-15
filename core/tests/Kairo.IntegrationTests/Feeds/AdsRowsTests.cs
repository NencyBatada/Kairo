using System.Text.Json;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class AdsRowsTests
{
    private static readonly AdsAccount Account = new("1111111111", "Asia/Kolkata", "INR");

    [Fact]
    public void A_campaign_row_turns_micros_into_money_and_keeps_ids_as_text()
    {
        var row = Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001","name":"Diwali","advertisingChannelType":"SEARCH","status":"ENABLED"},"metrics":{"impressions":"1000","clicks":"50","costMicros":"1234567","conversions":2.5,"conversionsValue":99.99}}""");

        Assert.Equal(
            """{"date":"2026-10-01","customer_id":"1111111111","campaign_id":"9001","campaign_name":"Diwali","channel":"SEARCH","campaign_status":"ENABLED","currency":"INR","impressions":1000,"clicks":50,"cost":"1.234567","conversions":2.5,"conversions_value":"99.99"}""",
            AdsRows.Campaign(row, Account).ToJsonString());
    }

    [Fact]
    public void Missing_metrics_count_as_zero()
    {
        var mapped = AdsRows.AdGroup(Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"adGroup":{"id":"8001","name":"Shoes"}}"""), Account);

        Assert.Equal("8001", mapped["ad_group_id"]!.GetValue<string>());
        Assert.Equal("Shoes", mapped["ad_group_name"]!.GetValue<string>());
        Assert.Equal(0, mapped["impressions"]!.GetValue<long>());
        Assert.Equal("0", mapped["cost"]!.GetValue<string>());
        Assert.Equal(0d, mapped["conversions"]!.GetValue<double>());
        Assert.Equal("0", mapped["conversions_value"]!.GetValue<string>());
    }

    [Fact]
    public void An_ad_row_names_the_ad_and_its_type()
    {
        var mapped = AdsRows.Ad(
            Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"adGroup":{"id":"8001"},"adGroupAd":{"ad":{"id":"7001","type":"RESPONSIVE_SEARCH_AD"}},"metrics":{}}"""),
            Account);

        Assert.Equal("7001", mapped["ad_id"]!.GetValue<string>());
        Assert.Equal("RESPONSIVE_SEARCH_AD", mapped["ad_type"]!.GetValue<string>());
        Assert.Equal("8001", mapped["ad_group_id"]!.GetValue<string>());
    }

    [Fact]
    public void A_region_row_takes_its_name_from_the_geo_target()
    {
        var row = Parse($$$"""{"segments":{"date":"2026-10-01","{{{GoogleAdsReports.RegionJson}}}":"geoTargetConstants/21342"},"campaign":{"id":"9001"},"metrics":{}}""");
        var names = new Dictionary<string, GeoTarget> { ["geoTargetConstants/21342"] = new("21342", "Maharashtra", "IN") };

        var mapped = AdsRows.Region(row, Account, names);

        Assert.Equal("21342", mapped["region_id"]!.GetValue<string>());
        Assert.Equal("Maharashtra", mapped["region_name"]!.GetValue<string>());
        Assert.Equal("IN", mapped["country_code"]!.GetValue<string>());
    }

    [Fact]
    public void A_region_row_Google_does_not_place_in_a_state_has_no_region()
    {
        var mapped = AdsRows.Region(
            Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"metrics":{}}"""), Account, new Dictionary<string, GeoTarget>());

        Assert.True(mapped.ContainsKey("region_id"));
        Assert.Null(mapped["region_id"]);
        Assert.Null(mapped["region_name"]);
        Assert.Null(mapped["country_code"]);
    }

    [Fact]
    public void The_region_report_asks_for_the_window_and_where_people_were()
    {
        Assert.Equal(
            $"SELECT segments.date, campaign.id, {GoogleAdsReports.RegionField}, metrics.impressions, metrics.clicks, metrics.cost_micros, "
            + "metrics.conversions, metrics.conversions_value FROM geographic_view "
            + "WHERE segments.date BETWEEN '2026-10-07' AND '2026-10-13' AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'",
            AdsQueries.Regions(new FeedWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13))));
    }

    [Fact]
    public void The_city_report_asks_for_the_window_and_where_people_were()
    {
        Assert.Equal(
            $"SELECT segments.date, campaign.id, {GoogleAdsReports.CityField}, metrics.impressions, metrics.clicks, metrics.cost_micros, "
            + "metrics.conversions, metrics.conversions_value FROM geographic_view "
            + "WHERE segments.date BETWEEN '2026-10-07' AND '2026-10-13' AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'",
            AdsQueries.Cities(new FeedWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13))));
    }

    [Fact]
    public void A_city_row_names_the_city_and_its_state()
    {
        var row = Parse("""{"segments":{"date":"2026-10-13","geoTargetCity":"geoTargetConstants/1007785"},"campaign":{"id":"9001"},"metrics":{"impressions":"10","clicks":"2","costMicros":"1500000","conversions":1.0,"conversionsValue":250.0}}""");
        var names = new Dictionary<string, GeoTarget>
        {
            ["geoTargetConstants/1007785"] = new("1007785", "Mumbai", "IN", "Mumbai,Maharashtra,India"),
        };

        var city = AdsRows.City(row, new AdsAccount("1111111111", "Asia/Kolkata", "INR"), names);

        Assert.Equal("1007785", city["city_id"]!.GetValue<string>());
        Assert.Equal("Mumbai", city["city_name"]!.GetValue<string>());
        Assert.Equal("Maharashtra", city["state_name"]!.GetValue<string>());
        Assert.Equal("IN", city["country_code"]!.GetValue<string>());
        Assert.Equal("1.5", city["cost"]!.GetValue<string>());
    }

    [Fact]
    public void A_city_Google_does_not_name_keeps_its_id()
    {
        var row = Parse("""{"segments":{"date":"2026-10-13","geoTargetCity":"geoTargetConstants/42"},"campaign":{"id":"9001"},"metrics":{}}""");

        var city = AdsRows.City(row, new AdsAccount("1111111111", "Asia/Kolkata", "INR"), new Dictionary<string, GeoTarget>());

        Assert.Equal("42", city["city_id"]!.GetValue<string>());
        Assert.Null(city["state_name"]);
    }

    [Fact]
    public void Geo_targets_are_looked_up_by_resource_name()
    {
        Assert.Equal(
            "SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.country_code, geo_target_constant.canonical_name FROM geo_target_constant "
            + "WHERE geo_target_constant.resource_name IN ('geoTargetConstants/1', 'geoTargetConstants/2')",
            AdsQueries.GeoTargets(["geoTargetConstants/1", "geoTargetConstants/2"]));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
