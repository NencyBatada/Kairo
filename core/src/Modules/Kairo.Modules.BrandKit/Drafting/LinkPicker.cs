using System.Text.RegularExpressions;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Drafting;

internal static partial class LinkPicker
{
    public const int MorePages = 8;

    private static readonly HashSet<string> Keywords =
        ["about", "story", "our-story", "mission", "collections", "collection", "shop", "category", "products", "men", "women", "kids"];

    private static readonly string[] SkippedExtensions =
        [".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".zip", ".mp4", ".css", ".js", ".xml", ".json"];

    public static IReadOnlyList<Uri> Pick(Uri home, IEnumerable<PageLink> links)
    {
        var homeKey = Key(home);
        var candidates = links
            .Where(link => string.Equals(link.Url.Host, home.Host, StringComparison.OrdinalIgnoreCase))
            .Where(link => Key(link.Url) != homeKey && link.Url.AbsoluteUri.Length <= KitContentRules.UrlLength)
            .Where(link => !SkippedExtensions.Any(extension => link.Url.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(link => Key(link.Url))
            .ToList();
        return candidates.Where(IsPreferred)
            .Concat(candidates.Where(link => !IsPreferred(link)))
            .Select(link => link.Url)
            .Take(MorePages)
            .ToList();
    }

    private static bool IsPreferred(PageLink link)
    {
        var path = link.Url.AbsolutePath.ToLowerInvariant();
        return path.Split('/').Any(Keywords.Contains)
            || Words().Split($"{path} {link.Text.ToLowerInvariant()}").Any(Keywords.Contains);
    }

    private static string Key(Uri url) => $"{url.Host.ToLowerInvariant()}{url.AbsolutePath.TrimEnd('/')}{url.Query}";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Words();
}
