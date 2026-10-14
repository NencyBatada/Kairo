namespace Kairo.Modules.BrandKit.Kit;

internal enum ColourRole
{
    Primary,
    Secondary,
    Accent,
    Background,
    Text,
}

internal enum CompetitorPolicy
{
    Never,
    Neutral,
}

internal enum SourceKind
{
    Page,
    Ad,
}

internal sealed record KitSource(SourceKind Kind, string Ref);

internal sealed record KitVoice
{
    public string Summary { get; init; } = "";

    public IReadOnlyList<string> Traits { get; init; } = [];

    public IReadOnlyList<string> Dos { get; init; } = [];

    public IReadOnlyList<string> Donts { get; init; } = [];

    public IReadOnlyList<string> Examples { get; init; } = [];

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitColour
{
    public string Hex { get; init; } = "";

    public string Name { get; init; } = "";

    public ColourRole Role { get; init; }

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitLogo
{
    public Guid? FileId { get; init; }

    public string? SourceUrl { get; init; }
}

internal sealed record KitMessage
{
    public string Text { get; init; } = "";

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitCategory
{
    public string Name { get; init; } = "";

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitDisclaimer
{
    public string Text { get; init; } = "";

    public string When { get; init; } = "";
}

internal sealed record KitCompetitor
{
    public string Name { get; init; } = "";

    public CompetitorPolicy Policy { get; init; }
}

internal sealed record KitRules
{
    public IReadOnlyList<string> BannedWords { get; init; } = [];

    public IReadOnlyList<KitDisclaimer> RequiredDisclaimers { get; init; } = [];

    public IReadOnlyList<string> Tone { get; init; } = [];

    public IReadOnlyList<KitCompetitor> Competitors { get; init; } = [];
}

internal sealed record KitContent
{
    public KitVoice Voice { get; init; } = new();

    public IReadOnlyList<KitColour> Colours { get; init; } = [];

    public KitLogo? Logo { get; init; }

    public IReadOnlyList<KitMessage> KeyMessages { get; init; } = [];

    public IReadOnlyList<KitCategory> ProductCategories { get; init; } = [];

    public KitRules Rules { get; init; } = new();

    public IReadOnlyList<Guid> GuidelineFileIds { get; init; } = [];

    public List<Guid> FileIds()
    {
        var ids = new List<Guid>();
        if (Logo?.FileId is { } logo)
        {
            ids.Add(logo);
        }

        foreach (var id in GuidelineFileIds)
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public KitContent WithoutFile(Guid fileId) => this with
    {
        Logo = Logo is { FileId: var id } logo && id == fileId
            ? logo.SourceUrl is null ? null : logo with { FileId = null }
            : Logo,
        GuidelineFileIds = GuidelineFileIds.Where(id => id != fileId).ToList(),
    };
}
