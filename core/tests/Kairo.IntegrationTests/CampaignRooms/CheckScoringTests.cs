using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Checks;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CheckScoringTests
{
    private static readonly EffectiveCheckSettings Settings = EffectiveCheckSettings.From(new CheckSettings
    {
        Version = 4,
        Rules = [new CheckRuleSetting { Rule = CheckRules.AdsPolicy, Severity = CheckSeverities.Off }],
        CustomRules = [new CustomCheckRule { Id = "custom.feet", Text = "Never show bare feet", AppliesTo = CheckTargets.Image, Severity = CheckSeverities.Warn }],
        PassScore = 80,
        FixAttempts = 3,
    });

    [Fact]
    public void Severities_come_from_the_settings_and_unknown_or_off_rules_are_dropped()
    {
        var scored = CheckScoring.Score(
            [
                new RawFinding(CheckRules.Voice, "Sounds pushy.", "headlines", 0),
                new RawFinding(CheckRules.AdsPolicy, "Off."),
                new RawFinding("made.up", "Unknown."),
                new RawFinding("custom.feet", "Shows bare feet."),
                new RawFinding(CheckRules.Political, CodeChecks.PoliticalMessage),
            ],
            Settings);

        Assert.Equal(
            [(CheckRules.Political, "Political content", CheckSeverities.Block, false), (CheckRules.Voice, "Brand voice", CheckSeverities.Warn, true), ("custom.feet", "Never show bare feet", CheckSeverities.Warn, true)],
            scored.Issues.Select(issue => (issue.Rule, issue.RuleName, issue.Severity, issue.Overridable)));
        Assert.Equal((50, false), (scored.Score, scored.Passed));
    }

    [Fact]
    public void Warnings_pass_until_the_score_drops_below_the_pass_score()
    {
        var two = CheckScoring.Score([new RawFinding(CheckRules.Voice, "One."), new RawFinding(CheckRules.Visual, "Two.")], Settings);
        var three = CheckScoring.Score([new RawFinding(CheckRules.Voice, "One."), new RawFinding(CheckRules.Visual, "Two."), new RawFinding(CheckRules.AbcdAttract, "Three.")], Settings);
        var clean = CheckScoring.Score([], Settings);

        Assert.Equal((80, true), (two.Score, two.Passed));
        Assert.Equal((70, false), (three.Score, three.Passed));
        Assert.Equal((100, true), (clean.Score, clean.Passed));
    }

    [Fact]
    public void Repeated_findings_count_once_and_long_text_is_cut()
    {
        var scored = CheckScoring.Score(
            [
                new RawFinding(CheckRules.Voice, "Sounds   pushy.", "headlines", 1),
                new RawFinding(CheckRules.Voice, "sounds pushy.", "headlines", 1),
                new RawFinding(CheckRules.Voice, new string('a', 400), Evidence: new string('b', 400)),
                new RawFinding(CheckRules.Voice, "   "),
            ],
            Settings);

        Assert.Equal(2, scored.Issues.Count);
        Assert.Equal("Sounds pushy.", scored.Issues[0].Message);
        Assert.Equal((CheckResultLimits.Message, CheckResultLimits.Evidence), (scored.Issues[1].Message.Length, scored.Issues[1].Evidence!.Length));
    }

    [Theory]
    [InlineData(AssetFormats.Square, 1200, 1200, "image/jpeg", 1000, 0)]
    [InlineData(AssetFormats.Landscape, 1200, 628, "image/png", 1000, 0)]
    [InlineData(AssetFormats.Portrait, 1200, 1200, "image/jpeg", 1000, 1)]
    [InlineData(AssetFormats.Square, 1200, 1200, "image/gif", 1000, 1)]
    [InlineData(AssetFormats.Square, 1200, 1200, "image/jpeg", 6_000_000, 1)]
    [InlineData(AssetFormats.Square, 1200, 1200, "image/jpeg", 0, 1)]
    public void Images_must_match_their_placement_size_type_and_file_limit(string format, int width, int height, string type, long bytes, int problems)
    {
        var asset = new Asset { Kind = AssetKinds.Image, Format = format, Width = width, Height = height };
        var content = new AssetContent { Media = new AssetMedia { FileKey = "drafts/a", ContentType = type, Bytes = bytes } };

        var found = CodeChecks.For(asset, content);

        Assert.Equal(problems, found.Count);
        Assert.All(found, finding => Assert.Equal(CheckRules.MediaSpec, finding.Rule));
    }

    [Theory]
    [InlineData(1080, 1920, 15, 0)]
    [InlineData(1920, 1080, 15, 1)]
    [InlineData(1080, 1920, 4, 1)]
    [InlineData(1080, 1920, 61, 1)]
    public void Videos_must_be_vertical_and_a_usable_length(int width, int height, int seconds, int problems)
    {
        var asset = new Asset { Kind = AssetKinds.Video, Width = width, Height = height, DurationSeconds = seconds };
        var content = new AssetContent { Media = new AssetMedia { FileKey = "drafts/v", ContentType = "video/mp4", Bytes = 10 } };

        Assert.Equal(problems, CodeChecks.For(asset, content).Count);
    }

    [Fact]
    public void Text_limits_and_political_words_are_found_in_code()
    {
        var asset = new Asset { Kind = AssetKinds.SearchAd };
        var content = new AssetContent
        {
            Headlines = ["Shoes For Election Day", "A Headline That Is Far Too Long For Search", "Dries Fast"],
            Descriptions = ["Mesh that dries fast.", "Made for the monsoon."],
        };

        var found = CodeChecks.For(asset, content);

        Assert.Equal([CheckRules.TextLimits, CheckRules.Political], found.Select(finding => finding.Rule));
        Assert.Equal("headlines", found[0].Field);
    }

    [Fact]
    public void A_fix_run_takes_a_few_assets_of_one_kind()
    {
        string[] kinds = [AssetKinds.SearchAd, AssetKinds.KeywordSet, AssetKinds.Image, AssetKinds.SearchAd, AssetKinds.ShoppingTitle, AssetKinds.DemandGenText, AssetKinds.KeywordSet];
        var (now, later) = AssetReworker.Budget(kinds, kind => kind);
        var (images, rest) = AssetReworker.Budget([AssetKinds.Image, AssetKinds.Image, AssetKinds.Image, AssetKinds.Video], kind => kind);
        var (video, _) = AssetReworker.Budget([AssetKinds.Video, AssetKinds.Video], kind => kind);

        Assert.Equal(AssetReworker.TextPerRun, now.Count);
        Assert.DoesNotContain(AssetKinds.Image, now);
        Assert.Equal(2, later.Count);
        Assert.Equal((2, 2), (images.Count, rest.Count));
        Assert.Single(video);
    }

    [Fact]
    public void The_fix_instruction_lists_blocking_issues_first_within_the_instruction_limit()
    {
        var issues = new List<CheckIssue>
        {
            new() { Rule = CheckRules.Voice, Severity = CheckSeverities.Warn, Message = "Sounds pushy." },
            new() { Rule = CheckRules.AdsPolicy, Severity = CheckSeverities.Block, Message = "Overclaims." },
        };
        issues.AddRange(Enumerable.Range(0, 20).Select(index => new CheckIssue { Rule = CheckRules.Voice, Severity = CheckSeverities.Warn, Message = $"Problem number {index} in the copy." }));

        var instruction = CheckFixWork.Instruction(issues);

        Assert.StartsWith($"{CheckFixWork.Lead} Overclaims. Sounds pushy.", instruction);
        Assert.True(instruction.Length <= AssetLimits.Instruction);
        Assert.Equal("Improve this asset so it follows the brand's rules.", CheckFixWork.Instruction([]));
    }
}
