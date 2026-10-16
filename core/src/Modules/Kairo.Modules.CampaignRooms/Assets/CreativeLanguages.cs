using Kairo.Modules.CampaignRooms.Briefs;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record CreativeLanguage(string Code, string Name, string Script, string? Voice = null, string? Font = null)
{
    public bool Speaks => Voice is not null && Font is not null;
}

internal static class CreativeLanguages
{
    public const string None = "zxx";
    public const string Latin = "latin";
    public const string Devanagari = "devanagari";

    public static readonly CreativeLanguage[] All =
    [
        new("en", "English", Latin, "en-IN-Chirp3-HD-Achird", "Noto Sans"),
        new("hi", "Hindi", Devanagari, "hi-IN-Chirp3-HD-Achird", "Noto Sans Devanagari"),
        new("mr", "Marathi", Devanagari),
        new("gu", "Gujarati", "gujarati"),
        new("ta", "Tamil", "tamil"),
        new("te", "Telugu", "telugu"),
        new("kn", "Kannada", "kannada"),
        new("ml", "Malayalam", "malayalam"),
        new("bn", "Bengali", "bengali"),
        new("pa", "Punjabi", "gurmukhi"),
        new("or", "Odia", "oriya"),
    ];

    private static readonly (string Script, int Start, int End)[] Ranges =
    [
        (Latin, 0x0041, 0x005A), (Latin, 0x0061, 0x007A), (Latin, 0x00C0, 0x024F),
        (Devanagari, 0x0900, 0x097F), (Devanagari, 0xA8E0, 0xA8FF), (Devanagari, 0x1CD0, 0x1CFF),
        ("bengali", 0x0980, 0x09FF), ("gurmukhi", 0x0A00, 0x0A7F), ("gujarati", 0x0A80, 0x0AFF),
        ("oriya", 0x0B00, 0x0B7F), ("tamil", 0x0B80, 0x0BFF), ("telugu", 0x0C00, 0x0C7F),
        ("kannada", 0x0C80, 0x0CFF), ("malayalam", 0x0D00, 0x0D7F),
    ];

    public static CreativeLanguage? For(string code) => All.FirstOrDefault(language => language.Code == code);

    public static bool Speaks(string code) => For(code)?.Speaks == true;

    public static string Name(string code) => For(code)?.Name ?? BriefChoices.LanguageName(code);

    public static List<string> Of(BriefContent content, string fallback)
    {
        var chosen = (content.Languages ?? []).Where(BriefChoices.IsLanguage).Distinct().Take(BriefLimits.Languages).ToList();
        return chosen.Count > 0 ? chosen : [fallback];
    }

    public static string? ScriptOf(string text)
    {
        var counts = new Dictionary<string, int>();
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            foreach (var (script, start, end) in Ranges)
            {
                if (value >= start && value <= end)
                {
                    counts[script] = counts.GetValueOrDefault(script) + 1;
                    break;
                }
            }
        }

        return counts.Count == 0 ? null : counts.MaxBy(entry => entry.Value).Key;
    }

    public static string FeedLanguage(IEnumerable<string?> titles, IReadOnlyList<string> briefLanguages)
    {
        var script = ScriptOf(string.Join(' ', titles.OfType<string>())) ?? Latin;
        return briefLanguages.FirstOrDefault(code => For(code)?.Script == script)
            ?? All.First(language => language.Script == script).Code;
    }
}
