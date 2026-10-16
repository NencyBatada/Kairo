using System.Globalization;
using Kairo.Contracts.Catalogue;
using Kairo.Modules.Opportunities.Scoring;

namespace Kairo.Modules.Opportunities.Radar;

internal sealed record TrendPointView(string Month, long Searches)
{
    public static TrendPointView Of(TrendPoint point) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{point.Year:D4}-{point.Month:D2}"), point.Searches);
}

internal sealed record RegionView(string Id, string Name, long LastMonthSearches, double? Growth, bool Rising)
{
    public static RegionView Of(RegionDemand region) => new(region.Id, region.Name, region.LastMonthSearches, region.Growth, region.Rising);
}

internal sealed record MatchedProductView(
    Guid Id,
    string OfferId,
    string Title,
    string? Category,
    string? Link,
    string? ImageLink,
    decimal? Price,
    decimal? SalePrice,
    string? Currency)
{
    public static MatchedProductView Of(CatalogueProduct product) => new(
        product.Id,
        product.OfferId,
        product.Title,
        product.Category,
        product.Link,
        product.ImageLink,
        product.Price,
        product.SalePrice,
        product.Currency);
}

internal sealed record ReasonSourceView(string Title, string Url);

internal sealed record ReasonView(string Text, ReasonSourceView[] Sources)
{
    public static ReasonView Of(SourcedReason reason) =>
        new(reason.Text, reason.Sources.Select(source => new ReasonSourceView(source.Title, source.Url)).ToArray());
}

internal sealed record TriggerRegionView(string Id, string Name);

internal sealed record TriggerView(
    Guid FiringId,
    Guid RuleId,
    string RuleName,
    string Source,
    string Summary,
    TriggerRegionView[] Regions,
    DateTimeOffset FiredAt,
    DateTimeOffset ConditionEndsAt,
    bool Ended)
{
    public static TriggerView? Of(OpportunityTrigger? trigger) => trigger is null
        ? null
        : new TriggerView(
            trigger.FiringId,
            trigger.RuleId,
            trigger.RuleName,
            trigger.Source,
            trigger.Summary,
            trigger.Regions.Select(region => new TriggerRegionView(region.Id, region.Name)).ToArray(),
            trigger.FiredAt,
            trigger.ConditionEndsAt,
            trigger.Ended);
}

internal sealed record BriefView(
    string Title,
    string Goal,
    string Audience,
    string KeyMessage,
    string? Offer,
    string[] Regions,
    Guid[] ProductIds,
    string[] Channels,
    string Timing,
    string? Model,
    string? PromptVersion,
    DateTimeOffset DraftedAt)
{
    public static BriefView? Of(OpportunityBrief? brief) => brief is null
        ? null
        : new BriefView(
            brief.Title,
            brief.Goal,
            brief.Audience,
            brief.KeyMessage,
            brief.Offer,
            [.. brief.Regions],
            [.. brief.ProductIds],
            [.. brief.Channels],
            brief.Timing,
            brief.Model,
            brief.PromptVersion,
            brief.DraftedAt);
}

internal sealed record ScoreBreakdownView(
    int Total,
    double Growth,
    int GrowthScore,
    double GrowthWeight,
    long LastMonthSearches,
    int VolumeScore,
    double VolumeWeight,
    double Fit,
    int FitScore,
    double FitWeight,
    string? FitReason,
    double Feedback,
    string[] FeedbackReasons,
    double Memory,
    string[] MemoryReasons);

internal sealed record OpportunityCard(
    Guid Id,
    string Topic,
    string Status,
    string Source,
    int Score,
    double Growth,
    double? Mom,
    double? Yoy,
    long LastMonthSearches,
    long AvgMonthlySearches,
    TrendPointView[] Trend,
    RegionView[] Regions,
    MatchedProductView[] Products,
    int ProductCount,
    string? Reason,
    bool Watched,
    bool Available,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? SavedAt,
    TriggerView? Trigger)
{
    public const int CardTrendMonths = 12;
    public const int CardRegions = 3;
    public const int CardProducts = 3;

    public static OpportunityCard Of(Opportunity opportunity, IReadOnlyList<CatalogueProduct> inStock, bool watched) => new(
        opportunity.Id,
        opportunity.Topic,
        opportunity.Status,
        opportunity.Source,
        opportunity.Score,
        opportunity.Growth,
        opportunity.Mom,
        opportunity.Yoy,
        opportunity.LastMonthSearches,
        opportunity.AvgMonthlySearches,
        opportunity.Trend.TakeLast(CardTrendMonths).Select(TrendPointView.Of).ToArray(),
        opportunity.Regions.Where(region => region.Rising).Take(CardRegions).Select(RegionView.Of).ToArray(),
        inStock.Take(CardProducts).Select(MatchedProductView.Of).ToArray(),
        inStock.Count,
        Headline(opportunity),
        watched,
        inStock.Count > 0,
        opportunity.FirstSeenAt,
        opportunity.LastSeenAt,
        opportunity.SavedAt,
        TriggerView.Of(opportunity.ActiveTrigger));

    public static string? Headline(Opportunity opportunity) => opportunity.Reasons.FirstOrDefault()?.Text ?? opportunity.FitReason;
}

internal sealed record OpportunityDetail(
    Guid Id,
    string Topic,
    string Status,
    string Source,
    int Score,
    double Growth,
    double? Mom,
    double? Yoy,
    long LastMonthSearches,
    long AvgMonthlySearches,
    TrendPointView[] Trend,
    RegionView[] Regions,
    MatchedProductView[] Products,
    string? Reason,
    bool Watched,
    bool Available,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? SavedAt,
    string[] Keywords,
    string[] Categories,
    ReasonView[] Reasons,
    string ReasonsStatus,
    ScoreBreakdownView ScoreBreakdown,
    DateTimeOffset? ClosedAt,
    string? DismissReason,
    string? DismissNote,
    Guid? RoomId,
    string? Model,
    string? PromptVersion,
    TriggerView? Trigger,
    TriggerView[] Triggers,
    BriefView? Brief,
    string? BriefStatus)
{
    public static OpportunityDetail Of(Opportunity opportunity, IReadOnlyList<CatalogueProduct> inStock, bool watched)
    {
        var breakdown = opportunity.Breakdown;
        return new OpportunityDetail(
            opportunity.Id,
            opportunity.Topic,
            opportunity.Status,
            opportunity.Source,
            opportunity.Score,
            opportunity.Growth,
            opportunity.Mom,
            opportunity.Yoy,
            opportunity.LastMonthSearches,
            opportunity.AvgMonthlySearches,
            opportunity.Trend.Select(TrendPointView.Of).ToArray(),
            opportunity.Regions.Select(RegionView.Of).ToArray(),
            inStock.Select(MatchedProductView.Of).ToArray(),
            OpportunityCard.Headline(opportunity),
            watched,
            inStock.Count > 0,
            opportunity.FirstSeenAt,
            opportunity.LastSeenAt,
            opportunity.SavedAt,
            [.. opportunity.Keywords],
            [.. opportunity.MatchedCategories],
            opportunity.Reasons.Select(ReasonView.Of).ToArray(),
            opportunity.ReasonsStatus,
            new ScoreBreakdownView(
                opportunity.Score,
                opportunity.Growth,
                breakdown.GrowthScore,
                OpportunityScoring.GrowthWeight,
                opportunity.LastMonthSearches,
                breakdown.VolumeScore,
                OpportunityScoring.VolumeWeight,
                breakdown.Fit,
                breakdown.FitScore,
                OpportunityScoring.FitWeight,
                opportunity.FitReason,
                breakdown.Feedback,
                [.. breakdown.FeedbackReasons],
                breakdown.Memory ?? 1,
                [.. breakdown.MemoryReasons ?? []]),
            opportunity.ClosedAt,
            opportunity.DismissReason,
            opportunity.DismissNote,
            opportunity.RoomId,
            opportunity.Model,
            opportunity.PromptVersion,
            TriggerView.Of(opportunity.ActiveTrigger),
            opportunity.Triggers.OrderByDescending(trigger => trigger.FiredAt).Select(trigger => TriggerView.Of(trigger)!).ToArray(),
            BriefView.Of(opportunity.Brief),
            opportunity.BriefStatus);
    }
}

internal sealed record OpportunityList(DateTimeOffset? RefreshedAt, string? LastOutcome, string? LastReason, OpportunityCard[] Items);

internal sealed record AcceptedView(Guid RoomId, OpportunityDetail Opportunity);

internal sealed record DismissRequest(string? Reason, string? Note);
