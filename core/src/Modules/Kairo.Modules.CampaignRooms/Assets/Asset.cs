using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed class Asset : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public string Key { get; set; } = "";

    public int BriefVersion { get; set; }

    public string Kind { get; set; } = "";

    public string CampaignType { get; set; } = "";

    public string Language { get; set; } = "";

    public string? AdGroup { get; set; }

    public string? Format { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public decimal? DurationSeconds { get; set; }

    public List<string> ProductIds { get; set; } = [];

    public bool Locked { get; set; }

    public Guid? LockedBy { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public bool Hidden { get; set; }

    public string CheckState { get; set; } = AssetCheckStates.Unchecked;

    public int CurrentVersion { get; set; }

    public string Status { get; set; } = AssetStatuses.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class AssetVersion : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public Guid AssetId { get; set; }

    public int Version { get; set; }

    public string Content { get; set; } = "{}";

    public string Origin { get; set; } = AssetOrigins.Generated;

    public string? Instruction { get; set; }

    public Guid? StageRunId { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? Provenance { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public AssetContent Read() => AssetContent.Parse(Content);

    public AssetProvenance ReadProvenance() => AssetProvenance.Parse(Provenance);
}

internal sealed class AssetContent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<AssetKeyword>? Keywords { get; set; }

    public List<string>? Headlines { get; set; }

    public List<string>? LongHeadlines { get; set; }

    public List<string>? Descriptions { get; set; }

    public string? Path1 { get; set; }

    public string? Path2 { get; set; }

    public string? FinalUrl { get; set; }

    public string? BusinessName { get; set; }

    public string? CallToAction { get; set; }

    public string? Title { get; set; }

    public string? CurrentTitle { get; set; }

    public string? OfferId { get; set; }

    public AssetMedia? Media { get; set; }

    public List<AssetScriptLine>? Script { get; set; }

    public static AssetContent Parse(string json) => JsonSerializer.Deserialize<AssetContent>(json, Json) ?? new AssetContent();

    public string Write() => JsonSerializer.Serialize(this, Json);

    public AssetContent Copy() => Parse(Write());

    public bool SameAs(AssetContent other) => Write() == other.Write();
}

internal sealed class AssetKeyword
{
    public string Text { get; set; } = "";

    public string Match { get; set; } = AdLimits.Phrase;
}

internal sealed class AssetMedia
{
    public string FileKey { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Bytes { get; set; }
}

internal sealed class AssetScriptLine
{
    public string Voiceover { get; set; } = "";

    public string Caption { get; set; } = "";

    public decimal Start { get; set; }

    public decimal End { get; set; }
}

internal static class AssetKinds
{
    public const string KeywordSet = "keyword_set";
    public const string SearchAd = "search_ad";
    public const string DemandGenText = "demand_gen_text";
    public const string ShoppingTitle = "shopping_title";
    public const string Image = "image";
    public const string Video = "video";
    public const string Voiceover = "voiceover";
    public const string Footage = "footage";

    public static readonly string[] Text = [KeywordSet, SearchAd, DemandGenText, ShoppingTitle];

    public static readonly string[] All = [KeywordSet, SearchAd, DemandGenText, ShoppingTitle, Image, Video, Voiceover, Footage];

    public static bool IsText(string kind) => Text.Contains(kind);

    public static int Order(string kind) => Array.IndexOf(All, kind) is var index and >= 0 ? index : All.Length;
}

internal static class AssetFormats
{
    public const string Landscape = "landscape";
    public const string Square = "square";
    public const string Portrait = "portrait";
    public const string Vertical = "vertical";
}

internal static class AssetOrigins
{
    public const string Generated = "generated";
    public const string Edited = "edited";
    public const string Regenerated = "regenerated";
    public const string Fixed = "fixed";
}

internal static class AssetCheckStates
{
    public const string Unchecked = "unchecked";
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Overridden = "overridden";
}

internal static class AssetStatuses
{
    public const string Active = "active";
    public const string Removed = "removed";
}

internal static class AssetLimits
{
    public const int Key = 200;
    public const int Instruction = 500;
    public const int MinInstruction = 2;
}
