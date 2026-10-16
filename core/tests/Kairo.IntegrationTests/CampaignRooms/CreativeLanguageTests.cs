using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CreativeLanguageTests
{
    [Fact]
    public void Every_brief_language_has_a_row_and_only_english_and_hindi_have_a_voice()
    {
        Assert.Equal(BriefChoices.Languages.Select(choice => choice.Value).Order(), CreativeLanguages.All.Select(language => language.Code).Order());
        Assert.Equal(["en", "hi"], CreativeLanguages.All.Where(language => language.Speaks).Select(language => language.Code));
        Assert.Equal(("hi-IN-Chirp3-HD-Achird", "Noto Sans Devanagari", "devanagari"), (CreativeLanguages.For("hi")!.Voice, CreativeLanguages.For("hi")!.Font, CreativeLanguages.For("hi")!.Script));
        Assert.False(CreativeLanguages.Speaks("mr"));
        Assert.Equal("Marathi", CreativeLanguages.Name("mr"));
    }

    [Theory]
    [InlineData("Acme Monsoon Runner", "latin")]
    [InlineData("एक्मे बारिश के जूते", "devanagari")]
    [InlineData("Acme बारिश के जूते और रनर", "devanagari")]
    [InlineData("மழை காலணி", "tamil")]
    [InlineData("2499", null)]
    public void The_main_script_of_a_text_is_the_one_with_most_letters(string text, string? script) =>
        Assert.Equal(script, CreativeLanguages.ScriptOf(text));

    [Fact]
    public void Shopping_titles_are_written_in_the_language_of_the_current_titles()
    {
        Assert.Equal("en", CreativeLanguages.FeedLanguage(["Acme Runner", "Rain Boot"], ["hi"]));
        Assert.Equal("hi", CreativeLanguages.FeedLanguage(["एक्मे रनर", "बारिश बूट"], ["en", "hi"]));
        Assert.Equal("mr", CreativeLanguages.FeedLanguage(["एक्मे रनर"], ["en", "mr"]));
        Assert.Equal("hi", CreativeLanguages.FeedLanguage(["एक्मे रनर"], ["en"]));
        Assert.Equal("en", CreativeLanguages.FeedLanguage([], ["hi"]));
    }

    [Fact]
    public void Hindi_counts_each_letter_and_vowel_sign_once_against_the_ads_limits()
    {
        Assert.Equal(13, AdLimits.Length("बारिश के जूते"));
        Assert.Equal(5, AdLimits.Length("हिंदी"));
        Assert.Equal(4, AdLimits.Length("雨靴"));
    }

    [Fact]
    public void Each_language_gets_its_own_copy_and_shopping_titles_go_with_the_feed_language()
    {
        var parts = CopyWork.Parts(["en", "hi"], ["search", "shopping", "demand_gen"], "en");

        Assert.Equal(
            [("en", "search,shopping,demand_gen", "keyword_set,search_ad,demand_gen_text,shopping_title"), ("hi", "search,demand_gen", "keyword_set,search_ad,demand_gen_text")],
            parts.Select(part => (part.Language, string.Join(",", part.CampaignTypes), string.Join(",", part.Kinds))));
    }

    [Fact]
    public void A_feed_language_outside_the_brief_gets_a_shopping_only_part()
    {
        var parts = CopyWork.Parts(["hi"], ["search", "shopping"], "en");

        Assert.Equal(
            [("hi", "search", "keyword_set,search_ad,demand_gen_text"), ("en", "shopping", "shopping_title")],
            parts.Select(part => (part.Language, string.Join(",", part.CampaignTypes), string.Join(",", part.Kinds))));
        Assert.Equal(["en"], CopyWork.Parts(["hi"], ["shopping"], "en").Select(part => part.Language));
    }

    [Fact]
    public void Demand_Gen_warns_about_languages_without_a_voiceover()
    {
        var content = Brief(["hi", "mr", "ta"], ["search", "demand_gen"]);
        var none = Brief(["mr"], ["demand_gen"]);

        var partly = Assert.Single(BriefChecks.Run(content, Situation()), warning => warning.Code == "no_voiceover");
        var silent = Assert.Single(BriefChecks.Run(none, Situation()), warning => warning.Code == "no_voiceover");

        Assert.Equal(("languages", "info"), (partly.Field, partly.Severity));
        Assert.Equal("Kairo can write the ads in Marathi and Tamil but can't make a voiceover in them yet, so the video will be in Hindi only.", partly.Message);
        Assert.Equal("warning", silent.Severity);
        Assert.Equal("Kairo can write the ads in Marathi but can't make a voiceover in it yet, so Demand Gen will run without a video. Add English or Hindi for a video.", silent.Message);
        Assert.DoesNotContain(BriefChecks.Run(Brief(["en", "hi"], ["demand_gen"]), Situation()), warning => warning.Code == "no_voiceover");
        Assert.DoesNotContain(BriefChecks.Run(Brief(["mr"], ["search"]), Situation()), warning => warning.Code == "no_voiceover");
    }

    private static readonly DateOnly Today = new(2026, 10, 15);

    private static BriefContent Brief(List<string> languages, List<string> types) => new()
    {
        Title = "Monsoon runners",
        Objective = "Sell waterproof shoes.",
        Kpi = "orders",
        CampaignTypes = types,
        Budget = new BriefBudget { Amount = 100000, Currency = "INR" },
        StartDate = Today.AddDays(1),
        EndDate = Today.AddDays(14),
        Regions = ["Maharashtra"],
        Audiences = ["Commuters"],
        KeyMessages = ["Stay dry."],
        ProductIds = ["a"],
        Languages = languages,
    };

    private static BriefSituation Situation() => new(
        Today,
        null,
        200000m,
        "INR",
        new Dictionary<string, string> { ["a"] = "Runner" },
        new HashSet<string> { "a" },
        true,
        true);
}
