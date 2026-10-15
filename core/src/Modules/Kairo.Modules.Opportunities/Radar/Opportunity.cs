namespace Kairo.Modules.Opportunities.Radar;

internal sealed class Opportunity : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Topic { get; set; } = "";

    public string TopicKey { get; set; } = "";

    public List<string> Keywords { get; set; } = [];

    public string Status { get; set; } = OpportunityStatuses.New;

    public string Source { get; set; } = OpportunitySources.Radar;

    public int Score { get; set; }

    public ScoreBreakdown Breakdown { get; set; } = new();

    public double Growth { get; set; }

    public double? Mom { get; set; }

    public double? Yoy { get; set; }

    public long LastMonthSearches { get; set; }

    public long AvgMonthlySearches { get; set; }

    public List<TrendPoint> Trend { get; set; } = [];

    public List<RegionDemand> Regions { get; set; } = [];

    public List<string> MatchedCategories { get; set; } = [];

    public List<Guid> MatchedProductIds { get; set; } = [];

    public string? FitReason { get; set; }

    public List<SourcedReason> Reasons { get; set; } = [];

    public string ReasonsStatus { get; set; } = ReasonsStatuses.Unavailable;

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public int MissedRefreshes { get; set; }

    public DateTimeOffset? SavedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    public string? DismissReason { get; set; }

    public string? DismissNote { get; set; }

    public double? GrowthAtClose { get; set; }

    public Guid? RoomId { get; set; }

    public bool IsOpen => Status is OpportunityStatuses.New or OpportunityStatuses.Saved;
}

internal sealed class ScoreBreakdown
{
    public int GrowthScore { get; set; }

    public int VolumeScore { get; set; }

    public int FitScore { get; set; }

    public double Fit { get; set; }

    public double Feedback { get; set; } = 1;

    public List<string> FeedbackReasons { get; set; } = [];
}

internal sealed class TrendPoint
{
    public int Year { get; set; }

    public int Month { get; set; }

    public long Searches { get; set; }
}

internal sealed class RegionDemand
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public long LastMonthSearches { get; set; }

    public double? Growth { get; set; }

    public bool Rising { get; set; }
}

internal sealed class SourcedReason
{
    public string Text { get; set; } = "";

    public List<ReasonSource> Sources { get; set; } = [];
}

internal sealed class ReasonSource
{
    public string Title { get; set; } = "";

    public string Url { get; set; } = "";
}

internal static class OpportunityStatuses
{
    public const string New = "new";
    public const string Saved = "saved";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";
    public const string Expired = "expired";
}

internal static class OpportunitySources
{
    public const string Radar = "radar";
}

internal static class ReasonsStatuses
{
    public const string Sourced = "sourced";
    public const string Unsourced = "unsourced";
    public const string Unavailable = "unavailable";
}

internal static class DismissReasons
{
    public const string NotRelevant = "not_relevant";
    public const string TooSmall = "too_small";
    public const string WrongTiming = "wrong_timing";
    public const string AlreadyCovered = "already_covered";
    public const string Other = "other";

    public static readonly string[] All = [NotRelevant, TooSmall, WrongTiming, AlreadyCovered, Other];
}
