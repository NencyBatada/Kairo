namespace Kairo.Modules.CampaignRooms.Checks;

internal static class CheckSeverities
{
    public const string Block = "block";

    public const string Warn = "warn";

    public const string Off = "off";

    public static readonly string[] All = [Block, Warn, Off];

    public static readonly string[] Custom = [Block, Warn];
}

internal static class CheckTargets
{
    public const string Text = "text";

    public const string Image = "image";

    public const string Video = "video";

    public const string All = "all";

    public static readonly string[] Kinds = [Text, Image, Video];

    public static readonly string[] Custom = [Text, Image, Video, All];

    public static bool Covers(string appliesTo, string target) => appliesTo == All || appliesTo == target;
}

internal sealed record CheckRule(
    string Id,
    string Name,
    string Description,
    string[] AppliesTo,
    string DefaultSeverity,
    bool Adjustable,
    bool Overridable);

internal static class CheckRules
{
    public const string TextLimits = "format.text_limits";
    public const string MediaSpec = "format.media_spec";
    public const string BannedWords = "brand.banned_words";
    public const string Competitors = "brand.competitors";
    public const string UnsupportedClaims = "policy.unsupported_claims";
    public const string Political = "policy.political";
    public const string Disclaimers = "brand.disclaimers";
    public const string Voice = "brand.voice";
    public const string Visual = "brand.visual";
    public const string AdsPolicy = "policy.ads";
    public const string ProductAccuracy = "accuracy.product";
    public const string LanguageMatch = "language.match";
    public const string AbcdAttract = "abcd.attract";
    public const string AbcdBrand = "abcd.brand";
    public const string AbcdConnect = "abcd.connect";
    public const string AbcdDirect = "abcd.direct";

    private static readonly string[] Text = [CheckTargets.Text];
    private static readonly string[] Media = [CheckTargets.Image, CheckTargets.Video];
    private static readonly string[] Video = [CheckTargets.Video];
    private static readonly string[] Spoken = [CheckTargets.Text, CheckTargets.Video];
    private static readonly string[] Everything = [CheckTargets.Text, CheckTargets.Image, CheckTargets.Video];

    public static IReadOnlyList<CheckRule> All { get; } =
    [
        Fixed(TextLimits, "Google Ads text limits", "Headlines, descriptions, paths, keywords and titles stay within Google Ads' length and count limits.", Text, overridable: false),
        Fixed(MediaSpec, "Image and video specs", "Images and videos have the exact size, shape, file type, file size and length each placement needs.", Media, overridable: false),
        Fixed(BannedWords, "Banned words", "No word from the Brand Kit's banned list appears in copy, on images or in the voiceover.", Everything, overridable: true),
        Fixed(Competitors, "Competitor mentions", "Competitors the Brand Kit says never to mention don't appear anywhere.", Everything, overridable: true),
        Fixed(UnsupportedClaims, "Unsupported prices, offers and claims", "Every price, offer and claim comes from the catalogue or the brief.", Everything, overridable: true),
        Fixed(Political, "Political content", "Ads don't touch elections, parties or political issues, which need extra verification Kairo doesn't do.", Everything, overridable: false),
        Adjustable(Disclaimers, "Required disclaimers", "Disclaimers the Brand Kit requires appear when their condition applies.", Spoken, CheckSeverities.Block),
        Adjustable(Voice, "Brand voice", "Copy and voiceover sound like the brand and follow the Brand Kit's dos and don'ts.", Spoken, CheckSeverities.Warn),
        Adjustable(Visual, "Brand visuals", "Images and videos use the brand's colours and show the logo correctly.", Media, CheckSeverities.Warn),
        Adjustable(AdsPolicy, "Google Ads policy risks", "No misleading, sensational or shocking content, misused trademarks, or punctuation and capitals Google Ads rejects.", Everything, CheckSeverities.Block),
        Adjustable(ProductAccuracy, "Product accuracy", "Products in images and videos match the catalogue photo in shape, colour and logo.", Media, CheckSeverities.Block),
        Adjustable(LanguageMatch, "Language and script", "Copy and voiceover are in the asset's language and script.", Spoken, CheckSeverities.Block),
        Adjustable(AbcdAttract, "ABCD: Attract", "The video grabs attention in its first 5 seconds.", Video, CheckSeverities.Warn),
        Adjustable(AbcdBrand, "ABCD: Brand", "The brand or product shows early and clearly.", Video, CheckSeverities.Warn),
        Adjustable(AbcdConnect, "ABCD: Connect", "The video shows a benefit or people that viewers can relate to.", Video, CheckSeverities.Warn),
        Adjustable(AbcdDirect, "ABCD: Direct", "The video ends with a clear call to action.", Video, CheckSeverities.Warn),
    ];

    private static readonly Dictionary<string, CheckRule> ById = All.ToDictionary(rule => rule.Id, StringComparer.Ordinal);

    public static CheckRule? Find(string id) => ById.GetValueOrDefault(id);

    private static CheckRule Fixed(string id, string name, string description, string[] appliesTo, bool overridable) =>
        new(id, name, description, appliesTo, CheckSeverities.Block, Adjustable: false, overridable);

    private static CheckRule Adjustable(string id, string name, string description, string[] appliesTo, string severity) =>
        new(id, name, description, appliesTo, severity, Adjustable: true, Overridable: true);
}
