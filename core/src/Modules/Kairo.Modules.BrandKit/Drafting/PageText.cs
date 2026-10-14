using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Kairo.Modules.BrandKit.Drafting;

internal static partial class PageText
{
    public const int MaxText = 6000;
    public const int MaxTitle = 300;
    public const int MaxDescription = 500;
    public const int MaxHeadings = 30;
    public const int MaxLine = 200;

    private const string Noise = "script, style, nav, footer, form, noscript, template, svg";

    static PageText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static IHtmlDocument Parse(byte[] body, string? charset = null)
    {
        if (!HasByteOrderMark(body) && EncodingOf(charset) is { } encoding)
        {
            return new HtmlParser().ParseDocument(encoding.GetString(body));
        }

        using var stream = new MemoryStream(body);
        return new HtmlParser().ParseDocument(stream);
    }

    public static PageContent Read(IHtmlDocument document, Uri url)
    {
        var headings = document.QuerySelectorAll("h1, h2, h3")
            .Select(heading => Cut(Clean(heading.TextContent), MaxLine))
            .Where(heading => heading.Length > 0)
            .Distinct()
            .Take(MaxHeadings)
            .ToList();
        return new PageContent(
            Cut(Clean(document.Title), MaxTitle),
            Cut(Clean(document.QuerySelector("meta[name=description]")?.GetAttribute("content")), MaxDescription),
            headings,
            Cut(MainText(document), MaxText),
            Links(document, url));
    }

    public static string Clean(string? text) => Whitespace().Replace(text ?? "", " ").Trim();

    public static string Cut(string text, int max) => text.Length > max ? text[..max].TrimEnd() : text;

    private static Encoding? EncodingOf(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool HasByteOrderMark(byte[] body)
    {
        var start = body.AsSpan();
        return start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])
            || start.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
            || start.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]);
    }

    private static string MainText(IHtmlDocument document)
    {
        if ((document.QuerySelector("main") ?? document.Body) is not { } root)
        {
            return "";
        }

        var copy = (IElement)root.Clone(deep: true);
        foreach (var noise in copy.QuerySelectorAll(Noise).ToList())
        {
            noise.Remove();
        }

        return Clean(string.Join(' ', copy.Descendants<IText>().Select(text => text.Data)));
    }

    private static List<PageLink> Links(IHtmlDocument document, Uri url)
    {
        var links = new List<PageLink>();
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            if (Uri.TryCreate(url, anchor.GetAttribute("href"), out var target)
                && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps))
            {
                links.Add(new PageLink(new Uri(target.GetLeftPart(UriPartial.Query)), Cut(Clean(anchor.TextContent), MaxLine)));
            }
        }

        return links;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
