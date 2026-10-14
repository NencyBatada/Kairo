namespace Kairo.Modules.BrandKit.Drafting;

internal sealed record PageLink(Uri Url, string Text);

internal sealed record PageContent(string Title, string Description, IReadOnlyList<string> Headings, string Text, IReadOnlyList<PageLink> Links);

internal sealed record ReadPage(string Id, Uri Url, string Title, string Description, IReadOnlyList<string> Headings, string Text);

internal sealed record ColourCandidate(string Hex, int Count, IReadOnlyList<string> From);

internal sealed record LogoCandidate(string Id, Uri Url, string From, string Alt);

internal sealed record WebsiteReading(IReadOnlyList<ReadPage> Pages, IReadOnlyList<ColourCandidate> Colours, IReadOnlyList<LogoCandidate> Logos);
