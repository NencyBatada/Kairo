using System.Text.Json;
using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;
using Kairo.Modules.Triggers.Rules;
using Kairo.Modules.Triggers.Weather;

namespace Kairo.IntegrationTests.Triggers;

public sealed class TriggerConditionTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly TimeZoneInfo India = Conditions.Zone("Asia/Kolkata");

    [Fact]
    public void Heavy_rain_counts_only_days_inside_the_window_at_or_above_the_threshold()
    {
        var condition = new RuleCondition { Kind = WeatherKinds.HeavyRain, Threshold = 50, WithinDays = 2 };
        List<ForecastDay> days = [Day(-1, 99), Day(0, 49.9), Day(1, 50), Day(2, 120)];

        var hits = Conditions.WeatherHits(condition, "mumbai", days, Today);

        Assert.Equal([new WeatherHit("mumbai", Today.AddDays(1), 50)], hits);
    }

    [Fact]
    public void A_heatwave_reads_the_highest_temperature()
    {
        var condition = new RuleCondition { Kind = WeatherKinds.Heatwave, Threshold = 42, WithinDays = 3 };
        List<ForecastDay> days = [Day(0, 80, 41), Day(1, 0, 43.5), new ForecastDay { Date = Today.AddDays(2), MaxCelsius = null }];

        var hits = Conditions.WeatherHits(condition, "new-delhi", days, Today);

        Assert.Equal([new WeatherHit("new-delhi", Today.AddDays(1), 43.5)], hits);
    }

    [Theory]
    [InlineData(7, "2026-10-13", false)]
    [InlineData(7, "2026-10-14", true)]
    [InlineData(0, "2026-10-20", false)]
    [InlineData(0, "2026-10-21", true)]
    [InlineData(0, "2026-10-23", true)]
    [InlineData(0, "2026-10-24", false)]
    public void A_festival_is_due_from_its_lead_time_until_it_ends(int daysBefore, string today, bool due)
    {
        var festival = new FestivalEvent { Name = "Shoe Fest", StartsOn = new DateOnly(2026, 10, 21), EndsOn = new DateOnly(2026, 10, 23) };

        Assert.Equal(due, Conditions.FestivalDue(new RuleCondition { DaysBefore = daysBefore }, festival, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_condition_ends_at_local_midnight_after_its_last_day()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 18, 30, 0, TimeSpan.Zero), Conditions.EndOf(new DateOnly(2026, 10, 15), India));
        Assert.Equal(new DateOnly(2026, 10, 15), Conditions.Today(new DateTimeOffset(2026, 10, 14, 19, 0, 0, TimeSpan.Zero), India));
    }

    [Fact]
    public void Summaries_name_the_heaviest_day_in_each_city()
    {
        var condition = new RuleCondition { Kind = WeatherKinds.HeavyRain, Threshold = 50 };

        var summary = Conditions.WeatherSummary(condition, [new("mumbai", Today, 55), new("mumbai", Today.AddDays(1), 62.5), new("pune", Today, 51)]);

        Assert.Equal("Heavy rain is forecast in Mumbai (62.5 mm on 15 Oct) and Pune (51 mm on 14 Oct).", summary);
    }

    [Theory]
    [InlineData("2026-10-14", "Diwali is 25 days away, on 8 Nov.")]
    [InlineData("2026-11-08", "Diwali starts today.")]
    [InlineData("2026-11-09", "Diwali is on now, until 10 Nov.")]
    public void Festival_summaries_count_down_to_the_day(string today, string expected)
    {
        var festival = new FestivalEvent { Name = "Diwali", StartsOn = new DateOnly(2026, 11, 8), EndsOn = new DateOnly(2026, 11, 10) };

        Assert.Equal(expected, Conditions.FestivalSummary(festival, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Rule_text_reads_naturally_for_each_source()
    {
        Assert.Equal(
            "When a heatwave of 42.5 °C or hotter is forecast within 1 day in New Delhi, suggest a campaign for Sandals. Waits 1 hour before firing again in the same region.",
            RuleText.Describe(TriggerSources.Weather, new RuleCondition { Kind = WeatherKinds.Heatwave, Threshold = 42.5, WithinDays = 1 }, ["new-delhi"], "Summer > Sandals", 1, null));
        Assert.Equal(
            "When Holi starts, suggest a campaign for Colours across India. Waits 2 days before firing again in the same region.",
            RuleText.Describe(TriggerSources.Festival, new RuleCondition { DaysBefore = 0 }, [], "Colours", 48, new FestivalEvent { Name = "Holi" }));
        Assert.Equal(
            "When Boots has 1,500 or more units in stock, suggest a campaign to clear them across India. Waits 12 hours before firing again in the same region.",
            RuleText.Describe(TriggerSources.Stock, new RuleCondition { MinUnits = 1500 }, [], "Boots", 12, null));
    }

    [Fact]
    public void Every_city_has_coordinates_in_india_and_a_known_state()
    {
        var states = IndiaRegions.All.Where(region => region.Kind == RegionKinds.State).Select(region => region.Id).ToHashSet();

        Assert.Equal(36, states.Count);
        Assert.Equal(IndiaRegions.All.Count, IndiaRegions.All.Select(region => region.Id).Distinct().Count());
        Assert.All(IndiaRegions.All.Where(region => region.Kind == RegionKinds.City), city =>
        {
            Assert.Contains(city.StateId!, states);
            Assert.InRange(city.Latitude!.Value, 6, 37);
            Assert.InRange(city.Longitude!.Value, 68, 98);
        });
        Assert.All(IndiaFestivals.All.SelectMany(festival => festival.Regions), region => Assert.Contains(region, states));
        Assert.All(IndiaFestivals.All, festival => Assert.True(festival.EndsOn >= festival.StartsOn));
    }

    [Fact]
    public void A_forecast_adds_up_day_and_night_rain()
    {
        using var document = JsonDocument.Parse("""
            {"forecastDays":[
              {"displayDate":{"year":2026,"month":10,"day":14},
               "daytimeForecast":{"precipitation":{"qpf":{"quantity":12.3,"unit":"MILLIMETERS"}}},
               "nighttimeForecast":{"precipitation":{"qpf":{"quantity":40,"unit":"MILLIMETERS"}}},
               "maxTemperature":{"degrees":29.4,"unit":"CELSIUS"}},
              {"displayDate":{"year":2026,"month":10,"day":15}},
              {"daytimeForecast":{}}
            ]}
            """);

        var days = WeatherClient.Parse(document.RootElement);

        Assert.Equal(2, days.Count);
        Assert.Equal((Today, 52.3, 29.4), (days[0].Date, days[0].RainMillimetres, days[0].MaxCelsius));
        Assert.Equal((0.0, (double?)null), (days[1].RainMillimetres, days[1].MaxCelsius));
    }

    [Fact]
    public void A_drafted_brief_keeps_only_known_regions_and_products()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var answer = new TriggerBriefAnswer(
            new DraftedBrief(" Monsoon ", "Sell", "Commuters", "Stay dry", " ", ["mumbai", "Goa"], ["p2", "p7", "p2"], ["search", "", "shopping"], "This week"),
            "gemini-test",
            "plan-trigger-brief/1");

        var brief = BrandChecker.BriefOf(answer, [("p1", first), ("p2", second)], ["Mumbai", "Pune"], DateTimeOffset.UnixEpoch)!;

        Assert.Equal(("Monsoon", null), (brief.Title, brief.Offer));
        Assert.Equal(["Mumbai"], brief.Regions);
        Assert.Equal([second], brief.ProductIds);
        Assert.Equal(["search", "shopping"], brief.Channels);
        Assert.Null(BrandChecker.BriefOf(answer with { Brief = answer.Brief! with { Goal = " " } }, [], [], DateTimeOffset.UnixEpoch));
    }

    private static ForecastDay Day(int offset, double rain, double? heat = null) =>
        new() { Date = Today.AddDays(offset), RainMillimetres = rain, MaxCelsius = heat };
}
