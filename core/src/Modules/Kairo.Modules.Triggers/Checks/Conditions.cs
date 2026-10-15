using System.Globalization;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Rules;
using Kairo.Modules.Triggers.Weather;

namespace Kairo.Modules.Triggers.Checks;

internal sealed record WeatherHit(string RegionId, DateOnly Day, double Value);

internal static class Conditions
{
    public const string IndiaTimeZone = "Asia/Kolkata";

    public static readonly TimeSpan StockHold = TimeSpan.FromHours(2);

    public static IReadOnlyList<WeatherHit> WeatherHits(RuleCondition condition, string regionId, IReadOnlyList<ForecastDay> days, DateOnly today)
    {
        var last = today.AddDays((condition.WithinDays ?? 1) - 1);
        var threshold = condition.Threshold ?? double.MaxValue;
        return days
            .Where(day => day.Date >= today && day.Date <= last)
            .Select(day => (day.Date, Value: condition.Kind == WeatherKinds.HeavyRain ? day.RainMillimetres : day.MaxCelsius))
            .Where(day => day.Value is { } value && value >= threshold)
            .Select(day => new WeatherHit(regionId, day.Date, day.Value!.Value))
            .ToList();
    }

    public static bool FestivalDue(RuleCondition condition, FestivalEvent festival, DateOnly today) =>
        today >= festival.StartsOn.AddDays(-(condition.DaysBefore ?? 0)) && today <= festival.EndsOn;

    public static TimeZoneInfo Zone(string? timeZoneId) =>
        !string.IsNullOrEmpty(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(IndiaTimeZone);

    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    public static DateTimeOffset EndOf(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    public static string Date(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    public static string WeatherSummary(RuleCondition condition, IReadOnlyList<WeatherHit> hits)
    {
        var heavyRain = condition.Kind == WeatherKinds.HeavyRain;
        var parts = hits
            .GroupBy(hit => hit.RegionId)
            .Select(group => group.MaxBy(hit => hit.Value)!)
            .Select(hit => $"{Regions.IndiaRegions.NameOf(hit.RegionId)} ({RuleText.Number(hit.Value)} {(heavyRain ? "mm" : "°C")} on {Date(hit.Day)})")
            .ToList();
        var places = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
        return heavyRain ? $"Heavy rain is forecast in {places}." : $"A heatwave is forecast in {places}.";
    }

    public static string FestivalSummary(FestivalEvent festival, DateOnly today)
    {
        var days = festival.StartsOn.DayNumber - today.DayNumber;
        return days switch
        {
            > 0 => $"{festival.Name} is {RuleText.Days(days)} away, on {Date(festival.StartsOn)}.",
            0 => $"{festival.Name} starts today.",
            _ => $"{festival.Name} is on now, until {Date(festival.EndsOn)}.",
        };
    }
}
