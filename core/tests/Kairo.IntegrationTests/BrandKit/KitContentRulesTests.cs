using System.Text.Json;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class KitContentRulesTests
{
    [Fact]
    public void An_empty_kit_is_valid()
    {
        var check = KitContentRules.Check(null);

        Assert.True(check.IsValid);
        Assert.Equal("", check.Content.Voice.Summary);
        Assert.Empty(check.Content.Colours);
        Assert.Null(check.Content.Logo);
        Assert.Empty(check.Content.Rules.BannedWords);
    }

    [Fact]
    public void Text_is_trimmed_and_blank_entries_are_dropped()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Summary = "  Warm.  ", Traits = [" bold ", "", "   "] },
            KeyMessages = [new KitMessage { Text = "  Free returns  " }, new KitMessage { Text = " " }],
            Rules = new KitRules { BannedWords = ["cheap", " "], Competitors = [new KitCompetitor { Name = " " }] },
        });

        Assert.True(check.IsValid);
        Assert.Equal("Warm.", check.Content.Voice.Summary);
        Assert.Equal(new[] { "bold" }, check.Content.Voice.Traits);
        Assert.Equal("Free returns", Assert.Single(check.Content.KeyMessages).Text);
        Assert.Equal(new[] { "cheap" }, check.Content.Rules.BannedWords);
        Assert.Empty(check.Content.Rules.Competitors);
    }

    [Fact]
    public void Null_lists_and_items_are_treated_as_empty()
    {
        var content = JsonSerializer.Deserialize<KitContent>(
            """{ "voice": { "summary": null, "traits": null }, "colours": null, "keyMessages": [null], "rules": null, "guidelineFileIds": null }""",
            KitJson.Options)!;

        var check = KitContentRules.Check(content);

        Assert.True(check.IsValid);
        Assert.Equal("", check.Content.Voice.Summary);
        Assert.Empty(check.Content.Voice.Traits);
        Assert.Empty(check.Content.Colours);
        Assert.Empty(check.Content.KeyMessages);
        Assert.Empty(check.Content.Rules.Tone);
        Assert.Empty(check.Content.GuidelineFileIds);
    }

    [Fact]
    public void Text_over_its_limit_is_reported_at_its_place()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Summary = new string('a', 501), Dos = ["ok", new string('b', 201)] },
        });

        Assert.Equal(new[] { "Use at most 500 characters." }, check.Errors["content.voice.summary"]);
        Assert.Equal(new[] { "Use at most 200 characters." }, check.Errors["content.voice.dos[1]"]);
    }

    [Fact]
    public void Too_many_entries_are_reported_on_the_list()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Traits = Enumerable.Range(1, 11).Select(i => $"trait {i}").ToList() },
        });

        Assert.Equal(new[] { "Add at most 10." }, check.Errors["content.voice.traits"]);
    }

    [Fact]
    public void Colours_are_upper_cased_and_duplicates_dropped()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Colours =
            [
                new KitColour { Hex = "#e4572e", Name = "Ember", Role = ColourRole.Primary },
                new KitColour { Hex = "#E4572E", Name = "Ember again", Role = ColourRole.Accent },
                new KitColour { Hex = "", Name = "" },
            ],
        });

        Assert.True(check.IsValid);
        var colour = Assert.Single(check.Content.Colours);
        Assert.Equal(("#E4572E", "Ember", ColourRole.Primary), (colour.Hex, colour.Name, colour.Role));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("E4572E")]
    public void A_colour_that_is_not_a_hex_code_is_reported(string hex)
    {
        var check = KitContentRules.Check(new KitContent
        {
            Colours = [new KitColour { Hex = "#000000", Name = "Ink" }, new KitColour { Hex = hex, Name = "Bad" }],
        });

        Assert.Equal(new[] { "Use a colour code like #E4572E." }, check.Errors["content.colours[1].hex"]);
    }

    [Fact]
    public void A_logo_with_neither_a_file_nor_an_address_is_dropped()
    {
        var check = KitContentRules.Check(new KitContent { Logo = new KitLogo { SourceUrl = "  " } });

        Assert.True(check.IsValid);
        Assert.Null(check.Content.Logo);
    }

    [Fact]
    public void Sources_must_be_web_addresses_or_ad_ids()
    {
        var check = KitContentRules.Check(new KitContent
        {
            KeyMessages =
            [
                new KitMessage
                {
                    Text = "Free returns",
                    Sources = [new KitSource(SourceKind.Page, "https://acme.example/returns"), new KitSource(SourceKind.Page, "javascript:alert(1)"), new KitSource(SourceKind.Ad, "12x")],
                },
            ],
            Logo = new KitLogo { SourceUrl = "ftp://acme.example/logo.png" },
        });

        Assert.Equal(new[] { "Use a full web address starting with https://." }, check.Errors["content.keyMessages[0].sources[1].ref"]);
        Assert.Equal(new[] { "Use the Google Ads ad ID, in digits." }, check.Errors["content.keyMessages[0].sources[2].ref"]);
        Assert.Equal(new[] { "Use a full web address starting with https://." }, check.Errors["content.logo.sourceUrl"]);
    }

    [Fact]
    public void Guideline_files_are_listed_once()
    {
        var file = Guid.NewGuid();

        var check = KitContentRules.Check(new KitContent { GuidelineFileIds = [file, file, Guid.Empty] });

        Assert.Equal(new[] { file }, check.Content.GuidelineFileIds);
    }

    [Fact]
    public void File_ids_put_the_logo_first_and_removing_a_file_clears_it_everywhere()
    {
        var (logo, guide) = (Guid.NewGuid(), Guid.NewGuid());
        var content = new KitContent
        {
            Logo = new KitLogo { FileId = logo, SourceUrl = "https://acme.example/logo.svg" },
            GuidelineFileIds = [guide],
        };

        Assert.Equal(new[] { logo, guide }, content.FileIds());
        var withoutLogo = content.WithoutFile(logo);
        Assert.Null(withoutLogo.Logo!.FileId);
        Assert.Equal("https://acme.example/logo.svg", withoutLogo.Logo.SourceUrl);
        Assert.Empty(content.WithoutFile(guide).GuidelineFileIds);
        Assert.Null(new KitContent { Logo = new KitLogo { FileId = logo } }.WithoutFile(logo).Logo);
    }

    [Fact]
    public void Stored_json_uses_camel_case_names_and_enum_values()
    {
        var json = KitJson.Write(new KitContent
        {
            Colours = [new KitColour { Hex = "#E4572E", Name = "Ember", Role = ColourRole.Background }],
            Rules = new KitRules { Competitors = [new KitCompetitor { Name = "Rival", Policy = CompetitorPolicy.Neutral }] },
        });

        var element = KitJson.Element(json);
        Assert.Equal("background", element.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal("neutral", element.GetProperty("rules").GetProperty("competitors")[0].GetProperty("policy").GetString());
        Assert.Equal(ColourRole.Background, KitJson.Read(json).Colours[0].Role);
        Assert.Equal("superseded", KitJson.Name(VersionStatus.Superseded));
    }
}
