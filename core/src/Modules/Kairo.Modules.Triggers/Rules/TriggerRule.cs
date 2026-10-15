namespace Kairo.Modules.Triggers.Rules;

internal sealed class TriggerRule : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Name { get; set; } = "";

    public string Source { get; set; } = TriggerSources.Weather;

    public RuleCondition Condition { get; set; } = new();

    public List<string> Regions { get; set; } = [];

    public string Category { get; set; } = "";

    public int CooldownHours { get; set; } = RuleLimits.DefaultCooldownHours;

    public string Status { get; set; } = RuleStatuses.Active;

    public string Origin { get; set; } = RuleOrigins.Manual;

    public string? Evidence { get; set; }

    public Guid? MemoryEntryId { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public string? LastCheckOutcome { get; set; }

    public Guid? LastImportId { get; set; }

    public DateTimeOffset? LastFiredAt { get; set; }
}

internal sealed class RuleCondition
{
    public string? Kind { get; set; }

    public double? Threshold { get; set; }

    public int? WithinDays { get; set; }

    public Guid? EventId { get; set; }

    public int? DaysBefore { get; set; }

    public long? MinUnits { get; set; }
}

internal static class TriggerSources
{
    public const string Weather = "weather";
    public const string Festival = "festival";
    public const string Stock = "stock";

    public static readonly string[] All = [Weather, Festival, Stock];
}

internal static class WeatherKinds
{
    public const string HeavyRain = "heavy_rain";
    public const string Heatwave = "heatwave";

    public static readonly string[] All = [HeavyRain, Heatwave];
}

internal static class RuleStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Deleted = "deleted";

    public static readonly string[] Listed = [Draft, Active, Paused];
}

internal static class RuleOrigins
{
    public const string Manual = "manual";
    public const string Suggested = "suggested";
}

internal static class CheckOutcomes
{
    public const string Fired = "fired";
    public const string Quiet = "quiet";
    public const string CoolingDown = "cooling_down";
    public const string NoStock = "no_stock";
    public const string NeedsQuantities = "needs_quantities";
    public const string WeatherUnavailable = "weather_unavailable";
    public const string EventPassed = "event_passed";
    public const string EventMissing = "event_missing";
    public const string Failed = "failed";
}

internal static class RuleLimits
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 80;
    public const int MaxCategoryLength = 750;
    public const int MaxRegions = 20;
    public const int MaxRules = 50;
    public const int DefaultCooldownHours = 72;
    public const int MinCooldownHours = 1;
    public const int MaxCooldownHours = 720;
    public const double DefaultRainMillimetres = 50;
    public const double MinRainMillimetres = 10;
    public const double MaxRainMillimetres = 300;
    public const double DefaultHeatCelsius = 40;
    public const double MinHeatCelsius = 30;
    public const double MaxHeatCelsius = 50;
    public const int DefaultWithinDays = 3;
    public const int MaxWithinDays = 7;
    public const int DefaultDaysBefore = 7;
    public const int MaxDaysBefore = 60;
    public const long MaxUnits = 100_000_000;
    public const int MaxEvidenceLength = 1000;
}
