using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed partial class ColourTally
{
    public const int Max = 12;

    private readonly Dictionary<string, Seen> _seen = [];

    public static string? Normalise(string value)
    {
        var text = value.Trim();
        if (HexValue().Match(text) is { Success: true } hex)
        {
            var digits = hex.Groups["digits"].Value;
            if (digits.Length == 3)
            {
                digits = string.Concat(digits.Select(digit => $"{digit}{digit}"));
            }

            return $"#{digits.ToUpperInvariant()}";
        }

        if (RgbValue().Match(text) is not { Success: true } rgb)
        {
            return null;
        }

        var channels = new[] { "r", "g", "b" }
            .Select(name => int.Parse(rgb.Groups[name].Value, CultureInfo.InvariantCulture))
            .ToArray();
        return channels.Any(channel => channel > 255) ? null : $"#{channels[0]:X2}{channels[1]:X2}{channels[2]:X2}";
    }

    public static bool IsNeutral(string hex)
    {
        var channels = new[] { 1, 3, 5 }
            .Select(start => int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0)
            .ToArray();
        var max = channels.Max();
        var min = channels.Min();
        var lightness = (max + min) / 2;
        var saturation = max == min ? 0 : (max - min) / (1 - Math.Abs((2 * lightness) - 1));
        return saturation < 0.10 || lightness < 0.08 || lightness > 0.95;
    }

    public void AddDocument(IHtmlDocument document)
    {
        foreach (var meta in document.QuerySelectorAll("meta[name=theme-color][content]"))
        {
            Add(meta.GetAttribute("content") ?? "", "theme-color");
        }

        foreach (var element in document.QuerySelectorAll("[style]"))
        {
            AddCss(element.GetAttribute("style") ?? "", "inline-style");
        }

        foreach (var style in document.QuerySelectorAll("style"))
        {
            AddCss(style.TextContent, "css");
        }
    }

    public void AddCss(string css, string from)
    {
        foreach (Match declaration in Declaration().Matches(css))
        {
            var source = declaration.Groups["name"].Value.StartsWith("--", StringComparison.Ordinal) ? "css-variable" : from;
            foreach (Match colour in Colour().Matches(declaration.Groups["value"].Value))
            {
                Add(colour.Value, source);
            }
        }
    }

    public IReadOnlyList<ColourCandidate> Top() => _seen
        .OrderByDescending(entry => entry.Value.Count)
        .ThenBy(entry => entry.Value.Order)
        .Take(Max)
        .Select(entry => new ColourCandidate(entry.Key, entry.Value.Count, entry.Value.From.Order(StringComparer.Ordinal).ToList()))
        .ToList();

    private void Add(string value, string from)
    {
        if (Normalise(value) is not { } hex || IsNeutral(hex))
        {
            return;
        }

        if (!_seen.TryGetValue(hex, out var seen))
        {
            seen = new Seen(_seen.Count);
            _seen[hex] = seen;
        }

        seen.Count++;
        seen.From.Add(from);
    }

    [GeneratedRegex(@"(?<name>-{0,2}[A-Za-z][A-Za-z0-9_-]*)\s*:\s*(?<value>[^;{}]+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}(?![0-9A-Fa-f])|#[0-9A-Fa-f]{3}(?![0-9A-Fa-f])|rgba?\([^)]*\)")]
    private static partial Regex Colour();

    [GeneratedRegex("^#(?<digits>[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$")]
    private static partial Regex HexValue();

    [GeneratedRegex(@"^rgba?\(\s*(?<r>\d{1,3})\s*[,\s]\s*(?<g>\d{1,3})\s*[,\s]\s*(?<b>\d{1,3})\s*([,/][^)]*)?\)$")]
    private static partial Regex RgbValue();

    private sealed class Seen(int order)
    {
        public int Order { get; } = order;

        public int Count { get; set; }

        public HashSet<string> From { get; } = [];
    }
}
