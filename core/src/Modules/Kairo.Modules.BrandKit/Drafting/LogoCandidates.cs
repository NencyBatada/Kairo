using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Drafting;

internal static class LogoCandidates
{
    public const int Max = 10;
    public const int MaxAlt = 300;

    private static readonly HashSet<string> OrganisationTypes = ["Organization", "Corporation", "OnlineStore", "Store"];

    public static IReadOnlyList<LogoCandidate> Find(IHtmlDocument document, Uri page)
    {
        var found = new List<(string? Address, string From, string? Alt)>();
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            found.AddRange(StructuredLogos(script.TextContent).Select(address => ((string?)address, "json-ld", (string?)null)));
        }

        found.AddRange(document.QuerySelectorAll("header img[src]").Select(image => (image.GetAttribute("src"), "header-img", image.GetAttribute("alt"))));
        found.AddRange(document.QuerySelectorAll("img[src]").Where(MentionsLogo).Select(image => (image.GetAttribute("src"), "logo-img", image.GetAttribute("alt"))));
        found.AddRange(document.QuerySelectorAll("link[rel~='apple-touch-icon'][href]").Select(link => (link.GetAttribute("href"), "apple-touch-icon", (string?)null)));
        found.AddRange(document.QuerySelectorAll("link[rel~='icon'][href]").Select(link => (link.GetAttribute("href"), "icon", (string?)null)));
        found.AddRange(document.QuerySelectorAll("meta[property='og:image'][content]").Select(meta => (meta.GetAttribute("content"), "og-image", (string?)null)));

        return found
            .Select(entry => (Url: Resolve(page, entry.Address), entry.From, entry.Alt))
            .Where(entry => entry.Url is not null)
            .DistinctBy(entry => entry.Url!.AbsoluteUri)
            .Take(Max)
            .Select((entry, index) => new LogoCandidate($"l{index + 1}", entry.Url!, entry.From, PageText.Cut(PageText.Clean(entry.Alt), MaxAlt)))
            .ToList();
    }

    private static bool MentionsLogo(IElement image) =>
        new[] { image.GetAttribute("src"), image.GetAttribute("alt"), image.GetAttribute("class"), image.Id }
            .Any(value => value?.Contains("logo", StringComparison.OrdinalIgnoreCase) == true);

    private static Uri? Resolve(Uri page, string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Uri.TryCreate(page, address.Trim(), out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            && url.AbsoluteUri.Length <= KitContentRules.UrlLength
                ? url
                : null;
    }

    private static List<string> StructuredLogos(string json)
    {
        var logos = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, logos);
        }
        catch (JsonException)
        {
            return [];
        }

        return logos;
    }

    private static void Walk(JsonElement element, List<string> logos)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, logos);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (IsOrganisation(element) && element.TryGetProperty("logo", out var logo))
        {
            if (logo.ValueKind == JsonValueKind.String && logo.GetString() is { } address)
            {
                logos.Add(address);
            }
            else if (logo.ValueKind == JsonValueKind.Object && logo.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            {
                logos.Add(url.GetString()!);
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name != "logo")
            {
                Walk(property.Value, logos);
            }
        }
    }

    private static bool IsOrganisation(JsonElement element) =>
        element.TryGetProperty("@type", out var type)
        && (type.ValueKind == JsonValueKind.String
            ? OrganisationTypes.Contains(type.GetString() ?? "")
            : type.ValueKind == JsonValueKind.Array
                && type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && OrganisationTypes.Contains(item.GetString() ?? "")));
}
