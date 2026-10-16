using Kairo.Modules.CampaignRooms.Assets;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record SaveCheckSettingsRequest(
    int? Version,
    Dictionary<string, string>? Rules,
    CustomCheckRuleRequest[]? CustomRules,
    int? PassScore,
    int? FixAttempts);

internal sealed record CustomCheckRuleRequest(string? Id, string? Text, string? AppliesTo, string? Severity);

internal sealed record ValidCheckSettings(
    List<CheckRuleSetting> Rules,
    List<CustomCheckRule> CustomRules,
    int PassScore,
    int FixAttempts);

internal static class CheckSettingsRules
{
    public static (ValidCheckSettings? Settings, Dictionary<string, string[]> Errors) Check(
        SaveCheckSettingsRequest request,
        IReadOnlyCollection<string> customIds)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not >= 0)
        {
            errors["version"] = ["Send the settings version from when you opened them."];
        }

        var rules = Rules(request.Rules, errors);
        var custom = Custom(request.CustomRules, customIds, errors);

        if (request.PassScore is not (>= 0 and <= 100))
        {
            errors["passScore"] = ["Enter a pass score from 0 to 100."];
        }

        if (request.FixAttempts is not (>= 0 and <= CheckLimits.MaxFixAttempts))
        {
            errors["fixAttempts"] = [$"Enter a number of fix attempts from 0 to {CheckLimits.MaxFixAttempts}."];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new ValidCheckSettings(rules, custom, request.PassScore!.Value, request.FixAttempts!.Value), errors);
    }

    private static List<CheckRuleSetting> Rules(Dictionary<string, string>? requested, Dictionary<string, string[]> errors)
    {
        var rules = new List<CheckRuleSetting>();
        foreach (var (id, severity) in (requested ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var key = $"rules.{id}";
            if (CheckRules.Find(id) is not { } rule)
            {
                errors[key] = ["This rule doesn't exist."];
            }
            else if (!rule.Adjustable)
            {
                if (severity != rule.DefaultSeverity)
                {
                    errors[key] = [$"{rule.Name} always blocks and can't be changed."];
                }
            }
            else if (!CheckSeverities.All.Contains(severity))
            {
                errors[key] = ["Choose block, warn or off."];
            }
            else
            {
                rules.Add(new CheckRuleSetting { Rule = id, Severity = severity });
            }
        }

        return rules;
    }

    private static List<CustomCheckRule> Custom(
        CustomCheckRuleRequest[]? requested,
        IReadOnlyCollection<string> customIds,
        Dictionary<string, string[]> errors)
    {
        var rules = new List<CustomCheckRule>();
        if (requested is null)
        {
            return rules;
        }

        if (requested.Length > CheckLimits.MaxCustomRules)
        {
            errors["customRules"] = [$"Add up to {CheckLimits.MaxCustomRules} custom rules."];
            return rules;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < requested.Length; index++)
        {
            var rule = requested[index];
            var key = $"customRules[{index}]";
            var text = AdLimits.Tidy(rule.Text);

            if (rule.Id is { } id && (!customIds.Contains(id) || !ids.Add(id)))
            {
                errors[$"{key}.id"] = ["This rule has changed. Reload the settings and try again."];
            }

            if (text.Length is < CheckLimits.MinCustomRuleText or > CheckLimits.MaxCustomRuleText)
            {
                errors[$"{key}.text"] = [$"Describe the rule in {CheckLimits.MinCustomRuleText} to {CheckLimits.MaxCustomRuleText} characters."];
            }
            else if (!texts.Add(text))
            {
                errors[$"{key}.text"] = ["This rule is already in the list."];
            }

            if (rule.AppliesTo is not { } appliesTo || !CheckTargets.Custom.Contains(appliesTo))
            {
                errors[$"{key}.appliesTo"] = ["Choose text, image, video or all."];
            }

            if (rule.Severity is not { } severity || !CheckSeverities.Custom.Contains(severity))
            {
                errors[$"{key}.severity"] = ["Choose block or warn."];
            }

            rules.Add(new CustomCheckRule
            {
                Id = rule.Id ?? $"{CheckLimits.CustomPrefix}{Guid.NewGuid():N}",
                Text = text,
                AppliesTo = rule.AppliesTo ?? "",
                Severity = rule.Severity ?? "",
            });
        }

        return rules;
    }
}
