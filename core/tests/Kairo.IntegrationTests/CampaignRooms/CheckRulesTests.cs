using System.Text.Json;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.Testing;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CheckRulesTests
{
    [Fact]
    public void Every_critical_evaluation_fixture_uses_a_production_blocking_rule()
    {
        var path = Path.GetFullPath(Path.Combine(ModuleFolders.ModulesDirectory, "..", "..", "..",
            "agents", "evaluations", "v1", "cases.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var critical = document.RootElement.GetProperty("guardian").EnumerateArray()
            .Where(item => item.TryGetProperty("critical", out var value) && value.GetBoolean())
            .ToArray();
        Assert.NotEmpty(critical);
        var settings = EffectiveCheckSettings.From(null);
        foreach (var item in critical)
        {
            var rule = item.GetProperty("expectedRule").GetString()!;
            Assert.NotNull(CheckRules.Find(rule));
            Assert.Equal(CheckSeverities.Block, settings.Severity(rule));
            Assert.False(CheckScore.Passes(1, 100, settings.PassScore));
        }
    }

    [Fact]
    public void Every_rule_has_a_unique_id_a_name_a_description_and_a_target()
    {
        Assert.Equal(CheckRules.All.Count, CheckRules.All.Select(rule => rule.Id).Distinct().Count());
        Assert.All(CheckRules.All, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
            Assert.NotEmpty(rule.AppliesTo);
            Assert.All(rule.AppliesTo, target => Assert.Contains(target, CheckTargets.Kinds));
            Assert.Contains(rule.DefaultSeverity, CheckSeverities.All);
        });
    }

    [Theory]
    [InlineData(CheckRules.TextLimits, false)]
    [InlineData(CheckRules.MediaSpec, false)]
    [InlineData(CheckRules.BannedWords, true)]
    [InlineData(CheckRules.Competitors, true)]
    [InlineData(CheckRules.UnsupportedClaims, true)]
    [InlineData(CheckRules.Political, false)]
    public void Fixed_rules_always_block(string id, bool overridable)
    {
        var rule = CheckRules.Find(id)!;

        Assert.False(rule.Adjustable);
        Assert.Equal(CheckSeverities.Block, rule.DefaultSeverity);
        Assert.Equal(overridable, rule.Overridable);
    }

    [Theory]
    [InlineData(CheckRules.Disclaimers, CheckSeverities.Block)]
    [InlineData(CheckRules.Voice, CheckSeverities.Warn)]
    [InlineData(CheckRules.Visual, CheckSeverities.Warn)]
    [InlineData(CheckRules.AdsPolicy, CheckSeverities.Block)]
    [InlineData(CheckRules.ProductAccuracy, CheckSeverities.Block)]
    [InlineData(CheckRules.LanguageMatch, CheckSeverities.Block)]
    [InlineData(CheckRules.AbcdAttract, CheckSeverities.Warn)]
    [InlineData(CheckRules.AbcdBrand, CheckSeverities.Warn)]
    [InlineData(CheckRules.AbcdConnect, CheckSeverities.Warn)]
    [InlineData(CheckRules.AbcdDirect, CheckSeverities.Warn)]
    public void Adjustable_rules_start_at_their_default(string id, string severity)
    {
        var rule = CheckRules.Find(id)!;

        Assert.True(rule.Adjustable);
        Assert.True(rule.Overridable);
        Assert.Equal(severity, rule.DefaultSeverity);
    }

    [Fact]
    public void Abcd_rules_apply_to_video_only()
    {
        Assert.All(
            CheckRules.All.Where(rule => rule.Id.StartsWith("abcd.", StringComparison.Ordinal)),
            rule => Assert.Equal([CheckTargets.Video], rule.AppliesTo));
    }

    [Fact]
    public void No_saved_settings_means_the_defaults_at_version_zero()
    {
        var settings = EffectiveCheckSettings.From(null);

        Assert.Equal(0, settings.Version);
        Assert.Equal(CheckLimits.DefaultPassScore, settings.PassScore);
        Assert.Equal(CheckLimits.DefaultFixAttempts, settings.FixAttempts);
        Assert.Empty(settings.CustomRules);
        Assert.All(settings.Rules, rule => Assert.Equal(rule.Rule.DefaultSeverity, rule.Severity));
        Assert.Equal(CheckRules.All.Count, settings.Rules.Count);
    }

    [Fact]
    public void Saved_severities_apply_to_adjustable_rules_only()
    {
        var settings = EffectiveCheckSettings.From(new CheckSettings
        {
            Version = 4,
            Rules =
            [
                new CheckRuleSetting { Rule = CheckRules.Voice, Severity = CheckSeverities.Off },
                new CheckRuleSetting { Rule = CheckRules.AdsPolicy, Severity = CheckSeverities.Warn },
                new CheckRuleSetting { Rule = CheckRules.Political, Severity = CheckSeverities.Off },
                new CheckRuleSetting { Rule = CheckRules.Visual, Severity = "sometimes" },
            ],
            PassScore = 70,
            FixAttempts = 1,
        });

        Assert.Equal(4, settings.Version);
        Assert.Equal(CheckSeverities.Off, settings.Severity(CheckRules.Voice));
        Assert.Equal(CheckSeverities.Warn, settings.Severity(CheckRules.AdsPolicy));
        Assert.Equal(CheckSeverities.Block, settings.Severity(CheckRules.Political));
        Assert.Equal(CheckSeverities.Warn, settings.Severity(CheckRules.Visual));
        Assert.Equal(70, settings.PassScore);
        Assert.Equal(1, settings.FixAttempts);
    }

    [Fact]
    public void Rules_that_are_off_or_for_other_assets_are_left_out()
    {
        var settings = EffectiveCheckSettings.From(new CheckSettings
        {
            Rules = [new CheckRuleSetting { Rule = CheckRules.Voice, Severity = CheckSeverities.Off }],
            CustomRules =
            [
                new CustomCheckRule { Id = "custom.a", Text = "Never show bare feet", AppliesTo = CheckTargets.Image, Severity = CheckSeverities.Block },
                new CustomCheckRule { Id = "custom.b", Text = "Say monsoon, not rain", AppliesTo = CheckTargets.All, Severity = CheckSeverities.Warn },
            ],
        });

        var text = settings.On(CheckTargets.Text).Select(rule => rule.Rule.Id).ToList();

        Assert.DoesNotContain(CheckRules.Voice, text);
        Assert.DoesNotContain(CheckRules.AbcdAttract, text);
        Assert.Contains(CheckRules.TextLimits, text);
        Assert.Contains(CheckRules.AbcdAttract, settings.On(CheckTargets.Video).Select(rule => rule.Rule.Id));
        Assert.Equal(["custom.b"], settings.CustomOn(CheckTargets.Text).Select(rule => rule.Id));
        Assert.Equal(["custom.a", "custom.b"], settings.CustomOn(CheckTargets.Image).Select(rule => rule.Id));
        Assert.Equal(CheckSeverities.Block, settings.Severity("custom.a"));
        Assert.Equal(CheckSeverities.Off, settings.Severity("custom.missing"));
    }

    [Theory]
    [InlineData(CheckRules.Political, false)]
    [InlineData(CheckRules.TextLimits, false)]
    [InlineData(CheckRules.BannedWords, true)]
    [InlineData(CheckRules.Voice, true)]
    [InlineData("custom.a", true)]
    public void Only_some_flags_can_be_overridden(string id, bool overridable)
    {
        Assert.Equal(overridable, EffectiveCheckSettings.From(null).Overridable(id));
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(0, 2, 80)]
    [InlineData(1, 0, 70)]
    [InlineData(2, 3, 10)]
    [InlineData(4, 0, 0)]
    public void The_score_starts_at_100_and_loses_points_for_each_issue(int blocking, int warnings, int score)
    {
        Assert.Equal(score, CheckScore.Of(blocking, warnings));
    }

    [Theory]
    [InlineData(0, 80, 80, true)]
    [InlineData(0, 70, 80, false)]
    [InlineData(1, 70, 0, false)]
    [InlineData(0, 0, 0, true)]
    public void An_asset_passes_with_no_blocking_issue_and_a_high_enough_score(int blocking, int score, int passScore, bool passes)
    {
        Assert.Equal(passes, CheckScore.Passes(blocking, score, passScore));
    }
}
