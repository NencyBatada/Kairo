using Kairo.Contracts.Brands;
using Kairo.Modules.BrandKit.Drafting;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class DraftAnswerMappingTests
{
    private static readonly WebsiteReading Reading = new(
        [
            new ReadPage("p1", new Uri("https://acme.example/"), "Acme", "Shoes", ["Acme Shoes"], "Free returns."),
            new ReadPage("p2", new Uri("https://acme.example/pages/our-story"), "Our story", "", [], "Founded in Pune."),
        ],
        [new ColourCandidate("#E4572E", 2, ["theme-color"]), new ColourCandidate("#1B998B", 1, ["css"])],
        [new LogoCandidate("l1", new Uri("https://acme.example/logo.png"), "header-img", "Acme")]);

    private static readonly BestAd[] Ads = [new BestAd("7001", ["Run further"], ["Free returns for 30 days."])];

    [Fact]
    public void The_request_numbers_pages_and_ads_and_lists_the_candidates()
    {
        var request = DraftAnswerMapping.Request(new BrandSummary(Guid.NewGuid(), "acme", "Acme Shoes", "INR", "Asia/Kolkata"), Reading, Ads);

        Assert.Equal(new AgentBrand("Acme Shoes", "INR"), request.Brand);
        Assert.Equal(new[] { "p1", "p2" }, request.Pages.Select(page => page.Id));
        Assert.Equal("https://acme.example/pages/our-story", request.Pages[1].Url);
        var ad = Assert.Single(request.Ads);
        Assert.Equal(("a1", "Run further"), (ad.Id, ad.Headlines[0]));
        Assert.Equal(new[] { "#E4572E", "#1B998B" }, request.ColourCandidates.Select(colour => colour.Hex));
        Assert.Equal(("l1", "https://acme.example/logo.png", "header-img", "Acme"), (request.LogoCandidates[0].Id, request.LogoCandidates[0].Url, request.LogoCandidates[0].From, request.LogoCandidates[0].Alt));
    }

    [Fact]
    public void Source_ids_map_back_to_page_addresses_and_ad_ids()
    {
        var outcome = DraftAnswerMapping.Map(Answer(), Reading, Ads)!;

        Assert.Equal(new[] { new KitSource(SourceKind.Page, "https://acme.example/") }, outcome.Content.Voice.Sources);
        Assert.Equal(new KitSource(SourceKind.Ad, "7001"), outcome.Content.KeyMessages[0].Sources.Single());
        Assert.Equal(new KitSource(SourceKind.Page, "https://acme.example/pages/our-story"), outcome.Content.ProductCategories[0].Sources.Single());
    }

    [Fact]
    public void Messages_and_categories_whose_sources_are_all_unknown_are_dropped()
    {
        var answer = Answer() with
        {
            KeyMessages = [new AnswerMessage("Free returns", ["a1"]), new AnswerMessage("Invented", ["p99", "a7"]), new AnswerMessage("Unsourced", [])],
            ProductCategories = [new AnswerCategory("Running shoes", ["p2", "p42"]), new AnswerCategory("Made up", ["x"])],
        };

        var outcome = DraftAnswerMapping.Map(answer, Reading, Ads)!;

        Assert.Equal(new[] { "Free returns" }, outcome.Content.KeyMessages.Select(message => message.Text));
        Assert.Equal(new[] { "Running shoes" }, outcome.Content.ProductCategories.Select(category => category.Name));
        Assert.Single(outcome.Content.ProductCategories[0].Sources);
    }

    [Fact]
    public void Keeps_only_candidate_colours_whatever_their_case()
    {
        var answer = Answer() with
        {
            Colours =
            [
                new AnswerColour("#e4572e", "Ember", "primary", ["p1"]),
                new AnswerColour("#E4572E", "Ember again", "accent", []),
                new AnswerColour("#123456", "Invented", "accent", ["p1"]),
                new AnswerColour("#1b998b", "Teal", "Highlight", ["p1"]),
                new AnswerColour("#1B998B", "Teal", "1", ["p1"]),
            ],
        };

        var outcome = DraftAnswerMapping.Map(answer, Reading, Ads)!;

        var colour = Assert.Single(outcome.Content.Colours);
        Assert.Equal(("#E4572E", "Ember", ColourRole.Primary), (colour.Hex, colour.Name, colour.Role));
    }

    [Fact]
    public void Lists_and_text_are_cut_to_the_kit_limits()
    {
        var answer = Answer() with
        {
            Voice = new AnswerVoice(new string('a', 900), Enumerable.Range(1, 30).Select(index => $"trait {index}").ToList(), [new string('b', 400)], [], [], ["p1"]),
            KeyMessages = Enumerable.Range(1, 30).Select(index => new AnswerMessage($"message {index}", ["p1"])).ToList(),
        };

        var outcome = DraftAnswerMapping.Map(answer, Reading, Ads)!;

        Assert.Equal(KitContentRules.SummaryLength, outcome.Content.Voice.Summary.Length);
        Assert.Equal(KitContentRules.Traits, outcome.Content.Voice.Traits.Count);
        Assert.Equal(KitContentRules.ShortText, outcome.Content.Voice.Dos[0].Length);
        Assert.Equal(KitContentRules.KeyMessages, outcome.Content.KeyMessages.Count);
    }

    [Fact]
    public void Picks_the_logo_candidate_the_answer_names()
    {
        Assert.Equal("l1", DraftAnswerMapping.Map(Answer(), Reading, Ads)!.Logo!.Id);
        Assert.Null(DraftAnswerMapping.Map(Answer() with { Logo = new AnswerLogo("l9") }, Reading, Ads)!.Logo);
        Assert.Null(DraftAnswerMapping.Map(Answer() with { Logo = null }, Reading, Ads)!.Logo);
    }

    [Fact]
    public void An_incomplete_answer_is_refused()
    {
        Assert.Null(DraftAnswerMapping.Map(Answer() with { Voice = null }, Reading, Ads));
        Assert.Null(DraftAnswerMapping.Map(Answer() with { Colours = null }, Reading, Ads));
        Assert.Null(DraftAnswerMapping.Map(Answer() with { Model = "" }, Reading, Ads));
        Assert.Null(DraftAnswerMapping.Map(Answer() with { PromptVersion = null }, Reading, Ads));
    }

    [Fact]
    public void The_draft_has_no_rules_or_guideline_files()
    {
        var content = DraftAnswerMapping.Map(Answer(), Reading, Ads)!.Content;

        Assert.Empty(content.Rules.BannedWords);
        Assert.Empty(content.GuidelineFileIds);
        Assert.Null(content.Logo);
    }

    private static DraftAnswer Answer() => new(
        new AnswerVoice("Friendly and practical.", ["warm"], ["Talk about comfort"], [], [], ["p1", "p1", "p77"]),
        [new AnswerColour("#E4572E", "Ember", "primary", ["p1"])],
        new AnswerLogo("l1"),
        [new AnswerMessage("Free returns for 30 days", ["a1"])],
        [new AnswerCategory("Running shoes", ["p2"])],
        "gemini-test",
        "brand-kit-draft/1");
}
