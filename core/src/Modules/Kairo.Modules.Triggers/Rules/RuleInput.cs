using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;

namespace Kairo.Modules.Triggers.Rules;

internal sealed record ConditionRequest(string? Kind, double? Threshold, int? WithinDays, Guid? EventId, int? DaysBefore, long? MinUnits);

internal sealed record RuleRequest(string? Name, string? Source, ConditionRequest? Condition, string[]? Regions, string? Category, int? CooldownHours);

internal sealed record RuleDraft(string Name, string Source, RuleCondition Condition, List<string> Regions, string Category, int CooldownHours, FestivalEvent? Event)
{
    public void ApplyTo(TriggerRule rule)
    {
        rule.Name = Name;
        rule.Source = Source;
        rule.Condition = Condition;
        rule.Regions = Regions;
        rule.Category = Category;
        rule.CooldownHours = CooldownHours;
    }
}

internal static class RuleInput
{
    public static async Task<(RuleDraft? Draft, Dictionary<string, string[]> Errors)> CheckAsync(
        RuleRequest request,
        TriggersDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < RuleLimits.MinNameLength or > RuleLimits.MaxNameLength)
        {
            errors["name"] = [$"Use {RuleLimits.MinNameLength} to {RuleLimits.MaxNameLength} characters."];
        }

        var source = request.Source?.Trim().ToLowerInvariant();
        if (source is null || !TriggerSources.All.Contains(source))
        {
            errors["source"] = ["Use weather, festival or stock."];
        }

        var category = request.Category?.Trim() ?? "";
        if (category.Length is 0 or > RuleLimits.MaxCategoryLength)
        {
            errors["category"] = ["Choose a product category."];
        }

        var cooldown = request.CooldownHours ?? RuleLimits.DefaultCooldownHours;
        if (cooldown is < RuleLimits.MinCooldownHours or > RuleLimits.MaxCooldownHours)
        {
            errors["cooldownHours"] = [$"Use {RuleLimits.MinCooldownHours} to {RuleLimits.MaxCooldownHours} hours."];
        }

        var regions = Regions(request.Regions, source, errors);
        var condition = new RuleCondition();
        FestivalEvent? festival = null;
        var asked = request.Condition ?? new ConditionRequest(null, null, null, null, null, null);
        switch (source)
        {
            case TriggerSources.Weather:
                condition = Weather(asked, errors);
                break;
            case TriggerSources.Festival:
                (condition, festival) = await FestivalAsync(asked, db, errors, cancellationToken);
                break;
            case TriggerSources.Stock:
                condition = Stock(asked, errors);
                break;
        }

        return errors.Count > 0
            ? (null, errors)
            : (new RuleDraft(name, source!, condition, regions, category, cooldown, festival), errors);
    }

    private static List<string> Regions(string[]? requested, string? source, Dictionary<string, string[]> errors)
    {
        var ids = (requested ?? []).Select(id => id?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList();
        var unknown = ids.Where(id => IndiaRegions.Find(id) is null).ToList();
        if (unknown.Count > 0)
        {
            errors["regions"] = [$"Kairo doesn't know {string.Join(", ", unknown.Take(3))}. Choose regions from the list."];
            return [];
        }

        if (source == TriggerSources.Weather)
        {
            if (ids.Count == 0 || ids.Any(id => IndiaRegions.Find(id) is not { HasForecast: true }))
            {
                errors["regions"] = ["Choose at least one city. Weather forecasts are checked by city."];
            }
        }

        ids.RemoveAll(id => id == IndiaRegions.CountryId);
        if (ids.Count > RuleLimits.MaxRegions)
        {
            errors["regions"] = [$"Choose up to {RuleLimits.MaxRegions} regions."];
        }

        return ids;
    }

    private static RuleCondition Weather(ConditionRequest asked, Dictionary<string, string[]> errors)
    {
        var kind = asked.Kind?.Trim().ToLowerInvariant();
        if (kind is null || !WeatherKinds.All.Contains(kind))
        {
            errors["condition.kind"] = ["Use heavy_rain or heatwave."];
            return new RuleCondition();
        }

        var (low, high, fallback, unit) = kind == WeatherKinds.HeavyRain
            ? (RuleLimits.MinRainMillimetres, RuleLimits.MaxRainMillimetres, RuleLimits.DefaultRainMillimetres, "mm")
            : (RuleLimits.MinHeatCelsius, RuleLimits.MaxHeatCelsius, RuleLimits.DefaultHeatCelsius, "°C");
        var threshold = asked.Threshold ?? fallback;
        if (double.IsNaN(threshold) || threshold < low || threshold > high)
        {
            errors["condition.threshold"] = [$"Use {low:0} to {high:0} {unit}."];
        }

        var within = asked.WithinDays ?? RuleLimits.DefaultWithinDays;
        if (within is < 1 or > RuleLimits.MaxWithinDays)
        {
            errors["condition.withinDays"] = [$"Use 1 to {RuleLimits.MaxWithinDays} days."];
        }

        return new RuleCondition { Kind = kind, Threshold = Math.Round(threshold, 1), WithinDays = within };
    }

    private static async Task<(RuleCondition Condition, FestivalEvent? Event)> FestivalAsync(
        ConditionRequest asked,
        TriggersDbContext db,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var daysBefore = asked.DaysBefore ?? RuleLimits.DefaultDaysBefore;
        if (daysBefore is < 0 or > RuleLimits.MaxDaysBefore)
        {
            errors["condition.daysBefore"] = [$"Use 0 to {RuleLimits.MaxDaysBefore} days."];
        }

        FestivalEvent? festival = null;
        if (asked.EventId is { } eventId)
        {
            festival = await db.BrandFestivals.AsNoTracking().SingleOrDefaultAsync(f => f.Id == eventId, cancellationToken);
        }

        if (festival is null)
        {
            errors["condition.eventId"] = ["Choose a festival or event from your calendar."];
        }

        return (new RuleCondition { EventId = festival?.Id, DaysBefore = daysBefore }, festival);
    }

    private static RuleCondition Stock(ConditionRequest asked, Dictionary<string, string[]> errors)
    {
        if (asked.MinUnits is not { } units || units < 1 || units > RuleLimits.MaxUnits)
        {
            errors["condition.minUnits"] = ["Use a number of units from 1 up."];
            return new RuleCondition();
        }

        return new RuleCondition { MinUnits = units };
    }
}
