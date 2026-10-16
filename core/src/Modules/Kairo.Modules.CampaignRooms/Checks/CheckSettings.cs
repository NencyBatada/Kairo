namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed class CheckSettings : IBrandOwned
{
    public Guid BrandId { get; set; }

    public int Version { get; set; }

    public List<CheckRuleSetting> Rules { get; set; } = [];

    public List<CustomCheckRule> CustomRules { get; set; } = [];

    public int PassScore { get; set; } = CheckLimits.DefaultPassScore;

    public int FixAttempts { get; set; } = CheckLimits.DefaultFixAttempts;

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class CheckRuleSetting
{
    public string Rule { get; set; } = "";

    public string Severity { get; set; } = "";
}

internal sealed class CustomCheckRule
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public string AppliesTo { get; set; } = "";

    public string Severity { get; set; } = "";
}

internal static class CheckLimits
{
    public const int DefaultPassScore = 80;
    public const int DefaultFixAttempts = 3;
    public const int MaxFixAttempts = 5;
    public const int MaxCustomRules = 20;
    public const int MinCustomRuleText = 3;
    public const int MaxCustomRuleText = 200;
    public const int BlockingPenalty = 30;
    public const int WarningPenalty = 10;
    public const string CustomPrefix = "custom.";
}

internal static class CheckScore
{
    public static int Of(int blocking, int warnings) =>
        Math.Max(0, 100 - CheckLimits.BlockingPenalty * blocking - CheckLimits.WarningPenalty * warnings);

    public static bool Passes(int blocking, int score, int passScore) => blocking == 0 && score >= passScore;
}

internal sealed record EffectiveCheckRule(CheckRule Rule, string Severity);

internal sealed record EffectiveCheckSettings(
    int Version,
    IReadOnlyList<EffectiveCheckRule> Rules,
    IReadOnlyList<CustomCheckRule> CustomRules,
    int PassScore,
    int FixAttempts,
    Guid? UpdatedBy,
    DateTimeOffset? UpdatedAt)
{
    public string Severity(string ruleId) =>
        Rules.FirstOrDefault(rule => rule.Rule.Id == ruleId)?.Severity
        ?? CustomRules.FirstOrDefault(rule => rule.Id == ruleId)?.Severity
        ?? CheckSeverities.Off;

    public bool Overridable(string ruleId) =>
        CheckRules.Find(ruleId) is not { } rule || rule.Overridable;

    public IReadOnlyList<EffectiveCheckRule> On(string target) =>
        Rules.Where(rule => rule.Severity != CheckSeverities.Off && rule.Rule.AppliesTo.Contains(target)).ToList();

    public IReadOnlyList<CustomCheckRule> CustomOn(string target) =>
        CustomRules.Where(rule => CheckTargets.Covers(rule.AppliesTo, target)).ToList();

    public static EffectiveCheckSettings From(CheckSettings? settings)
    {
        var chosen = (settings?.Rules ?? []).ToDictionary(rule => rule.Rule, rule => rule.Severity, StringComparer.Ordinal);
        var rules = CheckRules.All
            .Select(rule => new EffectiveCheckRule(
                rule,
                rule.Adjustable && chosen.TryGetValue(rule.Id, out var severity) && CheckSeverities.All.Contains(severity)
                    ? severity
                    : rule.DefaultSeverity))
            .ToList();

        return new EffectiveCheckSettings(
            settings?.Version ?? 0,
            rules,
            settings?.CustomRules.ToList() ?? [],
            settings?.PassScore ?? CheckLimits.DefaultPassScore,
            settings?.FixAttempts ?? CheckLimits.DefaultFixAttempts,
            settings?.UpdatedBy,
            settings?.UpdatedAt);
    }
}
