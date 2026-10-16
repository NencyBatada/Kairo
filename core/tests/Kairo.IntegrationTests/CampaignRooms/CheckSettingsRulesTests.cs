using Kairo.Modules.CampaignRooms.Checks;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CheckSettingsRulesTests
{
    [Fact]
    public void A_complete_request_is_accepted()
    {
        var (settings, errors) = CheckSettingsRules.Check(
            Request(
                rules: new() { [CheckRules.Voice] = CheckSeverities.Off, [CheckRules.Political] = CheckSeverities.Block },
                custom: [new CustomCheckRuleRequest(null, "  Never show   bare feet ", CheckTargets.Image, CheckSeverities.Block)]),
            []);

        Assert.Empty(errors);
        var rule = Assert.Single(settings!.Rules);
        Assert.Equal(CheckRules.Voice, rule.Rule);
        Assert.Equal(CheckSeverities.Off, rule.Severity);
        var custom = Assert.Single(settings.CustomRules);
        Assert.Equal("Never show bare feet", custom.Text);
        Assert.StartsWith(CheckLimits.CustomPrefix, custom.Id, StringComparison.Ordinal);
        Assert.Equal(75, settings.PassScore);
        Assert.Equal(2, settings.FixAttempts);
    }

    [Fact]
    public void Missing_rules_and_custom_rules_mean_none()
    {
        var (settings, errors) = CheckSettingsRules.Check(new SaveCheckSettingsRequest(0, null, null, 80, 3), []);

        Assert.Empty(errors);
        Assert.Empty(settings!.Rules);
        Assert.Empty(settings.CustomRules);
    }

    [Theory]
    [InlineData("brand.made_up", "rules.brand.made_up")]
    [InlineData(CheckRules.Political, "rules.policy.political")]
    [InlineData(CheckRules.Voice, "rules.brand.voice")]
    public void Unknown_fixed_or_badly_set_rules_are_refused(string id, string field)
    {
        var severity = id == CheckRules.Voice ? "sometimes" : CheckSeverities.Warn;

        var (settings, errors) = CheckSettingsRules.Check(Request(rules: new() { [id] = severity }), []);

        Assert.Null(settings);
        Assert.Equal([field], errors.Keys);
    }

    [Theory]
    [InlineData(null, 3, "passScore")]
    [InlineData(-1, 3, "passScore")]
    [InlineData(101, 3, "passScore")]
    [InlineData(80, null, "fixAttempts")]
    [InlineData(80, -1, "fixAttempts")]
    [InlineData(80, 6, "fixAttempts")]
    public void Thresholds_must_be_in_range(int? passScore, int? fixAttempts, string field)
    {
        var (_, errors) = CheckSettingsRules.Check(new SaveCheckSettingsRequest(0, null, null, passScore, fixAttempts), []);

        Assert.Equal([field], errors.Keys);
    }

    [Fact]
    public void The_version_is_required()
    {
        var (_, errors) = CheckSettingsRules.Check(new SaveCheckSettingsRequest(null, null, null, 80, 3), []);

        Assert.Equal(["version"], errors.Keys);
    }

    [Fact]
    public void Custom_rules_need_text_a_target_and_a_severity()
    {
        var (_, errors) = CheckSettingsRules.Check(
            Request(custom:
            [
                new CustomCheckRuleRequest(null, "no", CheckTargets.Text, CheckSeverities.Warn),
                new CustomCheckRuleRequest(null, new string('a', CheckLimits.MaxCustomRuleText + 1), CheckTargets.Text, CheckSeverities.Warn),
                new CustomCheckRuleRequest(null, "Say monsoon, not rain", "audio", CheckSeverities.Off),
                new CustomCheckRuleRequest(null, "SAY MONSOON, NOT RAIN", null, null),
            ]),
            []);

        Assert.Equal(
            [
                "customRules[0].text",
                "customRules[1].text",
                "customRules[2].appliesTo",
                "customRules[2].severity",
                "customRules[3].appliesTo",
                "customRules[3].severity",
                "customRules[3].text",
            ],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Custom_rules_are_capped()
    {
        var many = Enumerable.Range(0, CheckLimits.MaxCustomRules + 1)
            .Select(index => new CustomCheckRuleRequest(null, $"Rule number {index}", CheckTargets.All, CheckSeverities.Warn))
            .ToArray();

        var (_, errors) = CheckSettingsRules.Check(Request(custom: many), []);

        Assert.Equal(["customRules"], errors.Keys);
    }

    [Fact]
    public void Existing_custom_rules_keep_their_id_and_unknown_or_repeated_ids_are_refused()
    {
        var (kept, keptErrors) = CheckSettingsRules.Check(
            Request(custom: [new CustomCheckRuleRequest("custom.a", "Never show bare feet", CheckTargets.Image, CheckSeverities.Block)]),
            ["custom.a"]);
        var (_, errors) = CheckSettingsRules.Check(
            Request(custom:
            [
                new CustomCheckRuleRequest("custom.gone", "Never show bare feet", CheckTargets.Image, CheckSeverities.Block),
                new CustomCheckRuleRequest("custom.a", "Say monsoon, not rain", CheckTargets.Text, CheckSeverities.Warn),
                new CustomCheckRuleRequest("custom.a", "Show the sole", CheckTargets.Image, CheckSeverities.Warn),
            ]),
            ["custom.a"]);

        Assert.Empty(keptErrors);
        Assert.Equal("custom.a", Assert.Single(kept!.CustomRules).Id);
        Assert.Equal(["customRules[0].id", "customRules[2].id"], errors.Keys.Order(StringComparer.Ordinal));
    }

    private static SaveCheckSettingsRequest Request(
        Dictionary<string, string>? rules = null,
        CustomCheckRuleRequest[]? custom = null) =>
        new(0, rules, custom, 75, 2);
}
