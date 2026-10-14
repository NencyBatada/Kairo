using Kairo.Contracts.Brands;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed record DraftOutcome(KitContent Content, LogoCandidate? Logo);

internal static class DraftAnswerMapping
{
    public static DraftAgentRequest Request(BrandSummary brand, WebsiteReading reading, IReadOnlyList<BestAd> ads) => new(
        new AgentBrand(brand.Name, brand.Currency.Trim()),
        reading.Pages.Select(page => new AgentPage(page.Id, page.Url.AbsoluteUri, page.Title, page.Description, page.Headings, page.Text)).ToList(),
        ads.Select((ad, index) => new AgentAd(AdKey(index), ad.Headlines, ad.Descriptions)).ToList(),
        reading.Colours.Select(colour => new AgentColourCandidate(colour.Hex, colour.Count, colour.From)).ToList(),
        reading.Logos.Select(logo => new AgentLogoCandidate(logo.Id, logo.Url.AbsoluteUri, logo.From, logo.Alt)).ToList());

    public static DraftOutcome? Map(DraftAnswer answer, WebsiteReading reading, IReadOnlyList<BestAd> ads)
    {
        if (answer.Voice is not { } voice
            || answer.Colours is null
            || answer.KeyMessages is null
            || answer.ProductCategories is null
            || string.IsNullOrWhiteSpace(answer.Model)
            || string.IsNullOrWhiteSpace(answer.PromptVersion))
        {
            return null;
        }

        var sources = Sources(reading, ads);
        var candidates = reading.Colours.Select(colour => colour.Hex).ToHashSet(StringComparer.Ordinal);
        var content = new KitContent
        {
            Voice = new KitVoice
            {
                Summary = voice.Summary ?? "",
                Traits = Texts(voice.Traits),
                Dos = Texts(voice.Dos),
                Donts = Texts(voice.Donts),
                Examples = Texts(voice.Examples),
                Sources = SourcesOf(voice.Sources, sources),
            },
            Colours = answer.Colours
                .OfType<AnswerColour>()
                .Select(colour => Colour(colour, candidates, sources))
                .OfType<KitColour>()
                .DistinctBy(colour => colour.Hex)
                .ToList(),
            KeyMessages = answer.KeyMessages
                .OfType<AnswerMessage>()
                .Select(message => new KitMessage { Text = message.Text ?? "", Sources = SourcesOf(message.Sources, sources) })
                .Where(message => message.Sources.Count > 0)
                .ToList(),
            ProductCategories = answer.ProductCategories
                .OfType<AnswerCategory>()
                .Select(category => new KitCategory { Name = category.Name ?? "", Sources = SourcesOf(category.Sources, sources) })
                .Where(category => category.Sources.Count > 0)
                .ToList(),
        };

        var check = KitContentRules.Check(KitContentRules.Fit(content));
        if (!check.IsValid)
        {
            return null;
        }

        var logo = answer.Logo?.CandidateId is { } id ? reading.Logos.FirstOrDefault(candidate => candidate.Id == id) : null;
        return new DraftOutcome(check.Content, logo);
    }

    public static string AdKey(int index) => $"a{index + 1}";

    private static Dictionary<string, KitSource> Sources(WebsiteReading reading, IReadOnlyList<BestAd> ads)
    {
        var sources = reading.Pages.ToDictionary(page => page.Id, page => new KitSource(SourceKind.Page, page.Url.AbsoluteUri));
        for (var index = 0; index < ads.Count; index++)
        {
            sources[AdKey(index)] = new KitSource(SourceKind.Ad, ads[index].Id);
        }

        return sources;
    }

    private static List<KitSource> SourcesOf(IReadOnlyList<string?>? ids, Dictionary<string, KitSource> sources) =>
        (ids ?? [])
            .OfType<string>()
            .Select(id => id.Trim())
            .Distinct()
            .Select(id => sources.GetValueOrDefault(id))
            .OfType<KitSource>()
            .ToList();

    private static List<string> Texts(IReadOnlyList<string?>? values) => (values ?? []).OfType<string>().ToList();

    private static KitColour? Colour(AnswerColour colour, HashSet<string> candidates, Dictionary<string, KitSource> sources)
    {
        if (ColourTally.Normalise(colour.Hex ?? "") is not { } hex
            || !candidates.Contains(hex)
            || colour.Role is not { } roleName
            || !Enum.GetNames<ColourRole>().Contains(roleName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return new KitColour
        {
            Hex = hex,
            Name = colour.Name ?? "",
            Role = Enum.Parse<ColourRole>(roleName, ignoreCase: true),
            Sources = SourcesOf(colour.Sources, sources),
        };
    }
}
