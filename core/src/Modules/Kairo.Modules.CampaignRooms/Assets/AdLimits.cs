using System.Text;

namespace Kairo.Modules.CampaignRooms.Assets;

internal static class AdLimits
{
    public const int SearchHeadline = 30;
    public const int MinSearchHeadlines = 3;
    public const int MaxSearchHeadlines = 15;
    public const int SearchDescription = 90;
    public const int MinSearchDescriptions = 2;
    public const int MaxSearchDescriptions = 4;
    public const int SearchPath = 15;
    public const int Keyword = 80;
    public const int KeywordWords = 10;
    public const int MaxKeywords = 50;
    public const int DemandGenHeadline = 40;
    public const int DemandGenShortHeadline = 30;
    public const int MaxDemandGenHeadlines = 5;
    public const int DemandGenLongHeadline = 90;
    public const int MaxDemandGenLongHeadlines = 5;
    public const int DemandGenDescription = 90;
    public const int MaxDemandGenDescriptions = 5;
    public const int BusinessName = 25;
    public const int ShoppingTitle = 150;
    public const string Broad = "broad";
    public const string Phrase = "phrase";
    public const string Exact = "exact";

    public static readonly string[] MatchTypes = [Broad, Phrase, Exact];

    public static readonly string[] CallsToAction =
    [
        "learn_more", "shop_now", "buy_now", "order_now", "sign_up", "see_more", "visit_site",
        "book_now", "contact_us", "get_quote", "download", "subscribe", "apply_now",
    ];

    public static int Length(string text)
    {
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            length += Wide(rune.Value) ? 2 : 1;
        }

        return length;
    }

    public static Dictionary<string, string[]> Check(string kind, AssetContent content)
    {
        var errors = new Dictionary<string, string[]>();
        switch (kind)
        {
            case AssetKinds.KeywordSet:
                Keywords(content.Keywords ?? [], errors);
                break;
            case AssetKinds.SearchAd:
                Lines("headlines", "headline", content.Headlines, MinSearchHeadlines, MaxSearchHeadlines, SearchHeadline, errors);
                Lines("descriptions", "description", content.Descriptions, MinSearchDescriptions, MaxSearchDescriptions, SearchDescription, errors);
                Path("path1", content.Path1, errors);
                Path("path2", content.Path2, errors);
                if (content.Path2 is not null && content.Path1 is null)
                {
                    errors["path2"] = ["Add the first path before the second."];
                }

                break;
            case AssetKinds.DemandGenText:
                Lines("headlines", "headline", content.Headlines, 1, MaxDemandGenHeadlines, DemandGenHeadline, errors);
                if (!errors.ContainsKey("headlines") && !(content.Headlines ?? []).Any(text => Length(text) <= DemandGenShortHeadline))
                {
                    errors["headlines"] = [$"Keep at least one headline of up to {DemandGenShortHeadline} characters."];
                }

                Lines("longHeadlines", "long headline", content.LongHeadlines, 1, MaxDemandGenLongHeadlines, DemandGenLongHeadline, errors);
                Lines("descriptions", "description", content.Descriptions, 1, MaxDemandGenDescriptions, DemandGenDescription, errors);
                if (string.IsNullOrWhiteSpace(content.BusinessName) || Length(content.BusinessName) > BusinessName)
                {
                    errors["businessName"] = [$"Write a business name of up to {BusinessName} characters."];
                }

                if (content.CallToAction is not { } action || !CallsToAction.Contains(action))
                {
                    errors["callToAction"] = ["Choose one of the calls to action Google Ads offers."];
                }

                break;
            case AssetKinds.ShoppingTitle:
                if (string.IsNullOrWhiteSpace(content.Title) || Length(content.Title) > ShoppingTitle)
                {
                    errors["title"] = [$"Write a title of up to {ShoppingTitle} characters."];
                }

                break;
        }

        return errors;
    }

    public static string Tidy(string? text)
    {
        var builder = new StringBuilder();
        foreach (var word in (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word);
        }

        return builder.ToString();
    }

    private static void Lines(string field, string noun, List<string>? texts, int least, int most, int maximum, Dictionary<string, string[]> errors)
    {
        var lines = texts ?? [];
        if (lines.Count < least || lines.Count > most)
        {
            errors[field] = [least == most ? $"Write {least} {noun}s." : $"Write {least} to {most} {noun}s."];
        }
        else if (lines.Any(string.IsNullOrWhiteSpace))
        {
            errors[field] = [$"Fill in every {noun}."];
        }
        else if (lines.FirstOrDefault(text => Length(text) > maximum) is { } longer)
        {
            errors[field] = [$"Keep each {noun} to {maximum} characters. \"{longer}\" has {Length(longer)}."];
        }
        else if (lines.Distinct(StringComparer.OrdinalIgnoreCase).Count() != lines.Count)
        {
            errors[field] = [$"Make each {noun} different."];
        }
    }

    private static void Keywords(List<AssetKeyword> keywords, Dictionary<string, string[]> errors)
    {
        if (keywords.Count is < 1 or > MaxKeywords)
        {
            errors["keywords"] = [$"Keep 1 to {MaxKeywords} keywords."];
        }
        else if (keywords.FirstOrDefault(keyword => string.IsNullOrWhiteSpace(keyword.Text) || Length(keyword.Text) > Keyword) is { } longer)
        {
            errors["keywords"] = [$"Keep each keyword to {Keyword} characters. \"{longer.Text}\" breaks that."];
        }
        else if (keywords.FirstOrDefault(keyword => keyword.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > KeywordWords) is { } wordy)
        {
            errors["keywords"] = [$"Keep each keyword to {KeywordWords} words. \"{wordy.Text}\" has more."];
        }
        else if (keywords.Any(keyword => !MatchTypes.Contains(keyword.Match)))
        {
            errors["keywords"] = ["Use broad, phrase or exact match."];
        }
        else if (keywords.Select(keyword => keyword.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() != keywords.Count)
        {
            errors["keywords"] = ["Make each keyword different."];
        }
    }

    private static void Path(string field, string? path, Dictionary<string, string[]> errors)
    {
        if (path is null)
        {
            return;
        }

        if (path.Length == 0 || Length(path) > SearchPath || path.Any(char.IsWhiteSpace))
        {
            errors[field] = [$"Keep the path to {SearchPath} characters with no spaces."];
        }
    }

    private static bool Wide(int value) =>
        value is (>= 0x1100 and <= 0x115F)
            or (>= 0x2E80 and <= 0x303E)
            or (>= 0x3041 and <= 0x33FF)
            or (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xA000 and <= 0xA4CF)
            or (>= 0xAC00 and <= 0xD7A3)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0xFE30 and <= 0xFE4F)
            or (>= 0xFF00 and <= 0xFF60)
            or (>= 0xFFE0 and <= 0xFFE6)
            or (>= 0x20000 and <= 0x3FFFD);
}
