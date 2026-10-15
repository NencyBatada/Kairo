using System.Globalization;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;

namespace Kairo.Modules.Triggers.Rules;

internal static class RuleText
{
    public const int NamedRegions = 3;

    public static string Describe(string source, RuleCondition condition, IReadOnlyList<string> regions, string category, int cooldownHours, FestivalEvent? festival)
    {
        var product = Leaf(category);
        var when = source switch
        {
            TriggerSources.Weather when condition.Kind == WeatherKinds.HeavyRain =>
                $"When heavy rain of {Number(condition.Threshold)} mm or more in a day is forecast within {Days(condition.WithinDays ?? 1)} in {Places(regions)}, suggest a campaign for {product}.",
            TriggerSources.Weather =>
                $"When a heatwave of {Number(condition.Threshold)} °C or hotter is forecast within {Days(condition.WithinDays ?? 1)} in {Places(regions)}, suggest a campaign for {product}.",
            TriggerSources.Festival => Festival(condition, regions, product, festival),
            _ => $"When {product} has {Number(condition.MinUnits)} or more units in stock, suggest a campaign to clear them {Where(regions)}.",
        };
        return $"{when} Waits {Hours(cooldownHours)} before firing again in the same region.";
    }

    public static string Places(IReadOnlyList<string> regions)
    {
        var names = regions.Select(IndiaRegions.NameOf).ToList();
        return names.Count switch
        {
            0 => "India",
            1 => names[0],
            <= NamedRegions => $"{string.Join(", ", names[..^1])} or {names[^1]}",
            _ => $"{string.Join(", ", names.Take(NamedRegions))} and {names.Count - NamedRegions} more",
        };
    }

    public static string Joined(IReadOnlyList<string> regions)
    {
        var names = regions.Select(IndiaRegions.NameOf).ToList();
        return names.Count switch
        {
            0 => "India",
            1 => names[0],
            <= NamedRegions => $"{string.Join(", ", names[..^1])} and {names[^1]}",
            _ => $"{string.Join(", ", names.Take(NamedRegions))} and {names.Count - NamedRegions} more",
        };
    }

    public static string Leaf(string category) =>
        category.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? category;

    public static string Number(double? value) => (value ?? 0).ToString("0.#", CultureInfo.InvariantCulture);

    public static string Number(long? value) => (value ?? 0).ToString("N0", CultureInfo.InvariantCulture);

    public static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    public static string Hours(int hours) => hours % 24 == 0
        ? Days(hours / 24)
        : hours == 1 ? "1 hour" : $"{hours} hours";

    private static string Festival(RuleCondition condition, IReadOnlyList<string> regions, string product, FestivalEvent? festival)
    {
        var name = festival?.Name ?? "the festival";
        var places = regions.Count > 0 ? regions : festival?.Regions ?? [];
        var days = condition.DaysBefore ?? 0;
        var timing = days == 0 ? $"When {name} starts" : $"When {name} is {Days(days)} away";
        return $"{timing}, suggest a campaign for {product} {Where(places)}.";
    }

    private static string Where(IReadOnlyList<string> regions) => regions.Count == 0 ? "across India" : $"in {Places(regions)}";
}
