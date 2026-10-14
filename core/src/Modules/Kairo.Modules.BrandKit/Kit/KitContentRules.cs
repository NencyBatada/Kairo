using System.Text.RegularExpressions;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed record KitCheck(KitContent Content, Dictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static partial class KitContentRules
{
    public const int SummaryLength = 500;
    public const int ShortText = 200;
    public const int Traits = 10;
    public const int VoiceLines = 20;
    public const int Examples = 10;
    public const int Colours = 12;
    public const int ColourNameLength = 50;
    public const int KeyMessages = 20;
    public const int Categories = 50;
    public const int NameLength = 100;
    public const int BannedWords = 200;
    public const int BannedWordLength = 50;
    public const int Disclaimers = 20;
    public const int DisclaimerLength = 500;
    public const int ToneRules = 20;
    public const int Competitors = 50;
    public const int GuidelineFiles = 20;
    public const int SourcesPerItem = 10;
    public const int UrlLength = 2000;

    public const string BadColour = "Use a colour code like #E4572E.";
    public const string NotWebAddress = "Use a full web address starting with https://.";
    public const string NotAdId = "Use the Google Ads ad ID, in digits.";

    public static KitCheck Check(KitContent? input)
    {
        var errors = new Dictionary<string, string[]>();
        var content = input ?? new KitContent();
        var voice = content.Voice ?? new KitVoice();
        var rules = content.Rules ?? new KitRules();

        var normalized = new KitContent
        {
            Voice = new KitVoice
            {
                Summary = Text(voice.Summary, "content.voice.summary", SummaryLength, errors),
                Traits = Texts(voice.Traits, "content.voice.traits", Traits, ShortText, errors),
                Dos = Texts(voice.Dos, "content.voice.dos", VoiceLines, ShortText, errors),
                Donts = Texts(voice.Donts, "content.voice.donts", VoiceLines, ShortText, errors),
                Examples = Texts(voice.Examples, "content.voice.examples", Examples, ShortText, errors),
                Sources = Sources(voice.Sources, "content.voice.sources", errors),
            },
            Colours = Items(content.Colours, "content.colours", Colours, errors, (colour, path) =>
            {
                var hex = (colour.Hex ?? "").Trim();
                var name = Text(colour.Name, $"{path}.name", ColourNameLength, errors);
                if (hex.Length == 0 && name.Length == 0)
                {
                    return null;
                }

                if (!Hex().IsMatch(hex))
                {
                    errors[$"{path}.hex"] = [BadColour];
                }

                return colour with { Hex = hex.ToUpperInvariant(), Name = name, Sources = Sources(colour.Sources, $"{path}.sources", errors) };
            }).DistinctBy(colour => colour.Hex).ToList(),
            Logo = LogoOf(content.Logo, errors),
            KeyMessages = Items(content.KeyMessages, "content.keyMessages", KeyMessages, errors, (message, path) =>
            {
                var text = Text(message.Text, $"{path}.text", ShortText, errors);
                return text.Length == 0 ? null : message with { Text = text, Sources = Sources(message.Sources, $"{path}.sources", errors) };
            }),
            ProductCategories = Items(content.ProductCategories, "content.productCategories", Categories, errors, (category, path) =>
            {
                var name = Text(category.Name, $"{path}.name", NameLength, errors);
                return name.Length == 0 ? null : category with { Name = name, Sources = Sources(category.Sources, $"{path}.sources", errors) };
            }),
            Rules = new KitRules
            {
                BannedWords = Texts(rules.BannedWords, "content.rules.bannedWords", BannedWords, BannedWordLength, errors),
                RequiredDisclaimers = Items(rules.RequiredDisclaimers, "content.rules.requiredDisclaimers", Disclaimers, errors, (disclaimer, path) =>
                {
                    var text = Text(disclaimer.Text, $"{path}.text", DisclaimerLength, errors);
                    var when = Text(disclaimer.When, $"{path}.when", ShortText, errors);
                    return text.Length == 0 ? null : new KitDisclaimer { Text = text, When = when };
                }),
                Tone = Texts(rules.Tone, "content.rules.tone", ToneRules, ShortText, errors),
                Competitors = Items(rules.Competitors, "content.rules.competitors", Competitors, errors, (competitor, path) =>
                {
                    var name = Text(competitor.Name, $"{path}.name", NameLength, errors);
                    return name.Length == 0 ? null : competitor with { Name = name };
                }),
            },
            GuidelineFileIds = GuidelineFileIdsOf(content.GuidelineFileIds, errors),
        };

        return new KitCheck(normalized, errors);
    }

    public static bool IsWebAddress(string value) =>
        value.Length <= UrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Text(string? value, string path, int max, Dictionary<string, string[]> errors)
    {
        var text = (value ?? "").Trim();
        if (text.Length > max)
        {
            errors[path] = [$"Use at most {max} characters."];
        }

        return text;
    }

    private static List<string> Texts(IReadOnlyList<string?>? values, string path, int max, int length, Dictionary<string, string[]> errors)
    {
        var kept = new List<string>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var text = Text(list[i], $"{path}[{i}]", length, errors);
            if (text.Length > 0)
            {
                kept.Add(text);
            }
        }

        if (kept.Count > max)
        {
            errors[path] = [$"Add at most {max}."];
        }

        return kept;
    }

    private static List<T> Items<T>(
        IReadOnlyList<T?>? values,
        string path,
        int max,
        Dictionary<string, string[]> errors,
        Func<T, string, T?> check)
        where T : class
    {
        var kept = new List<T>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is { } item && check(item, $"{path}[{i}]") is { } normalized)
            {
                kept.Add(normalized);
            }
        }

        if (kept.Count > max)
        {
            errors[path] = [$"Add at most {max}."];
        }

        return kept;
    }

    private static List<KitSource> Sources(IReadOnlyList<KitSource?>? values, string path, Dictionary<string, string[]> errors)
    {
        var kept = new List<KitSource>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not { } source)
            {
                continue;
            }

            var reference = (source.Ref ?? "").Trim();
            if (source.Kind == SourceKind.Page && !IsWebAddress(reference))
            {
                errors[$"{path}[{i}].ref"] = [NotWebAddress];
            }
            else if (source.Kind == SourceKind.Ad && !AdId().IsMatch(reference))
            {
                errors[$"{path}[{i}].ref"] = [NotAdId];
            }
            else
            {
                kept.Add(source with { Ref = reference });
            }
        }

        if (kept.Count > SourcesPerItem)
        {
            errors[path] = [$"Add at most {SourcesPerItem}."];
        }

        return kept;
    }

    private static KitLogo? LogoOf(KitLogo? logo, Dictionary<string, string[]> errors)
    {
        if (logo is null)
        {
            return null;
        }

        var url = (logo.SourceUrl ?? "").Trim();
        if (logo.FileId is null && url.Length == 0)
        {
            return null;
        }

        if (url.Length > 0 && !IsWebAddress(url))
        {
            errors["content.logo.sourceUrl"] = [NotWebAddress];
        }

        return new KitLogo { FileId = logo.FileId, SourceUrl = url.Length == 0 ? null : url };
    }

    private static List<Guid> GuidelineFileIdsOf(IReadOnlyList<Guid>? values, Dictionary<string, string[]> errors)
    {
        var ids = (values ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > GuidelineFiles)
        {
            errors["content.guidelineFileIds"] = [$"Add at most {GuidelineFiles}."];
        }

        return ids;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    [GeneratedRegex("^[0-9]{1,20}$")]
    private static partial Regex AdId();
}
