using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Scoring;

namespace Kairo.Modules.Opportunities.Refresh;

internal sealed class RadarTopic
{
    public string Name { get; init; } = "";

    public string Key { get; init; } = "";

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public Demand Demand { get; init; } = new([]);

    public ScoreBreakdown Breakdown { get; init; } = new();

    public int Score { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IReadOnlyList<Guid> ProductIds { get; init; } = [];

    public string? FitReason { get; init; }

    public bool Watched { get; init; }

    public List<RegionDemand>? Regions { get; set; }

    public List<SourcedReason>? Reasons { get; set; }

    public string? ReasonsStatus { get; set; }

    public string? Model { get; init; }

    public string? PromptVersion { get; init; }
}

internal sealed record MergeResult(IReadOnlyList<Opportunity> Created, int Updated, int Expired);

internal static class OpportunityMerger
{
    public const double SameTopic = 0.5;
    public const double SharpRiseGap = 0.5;

    public static IReadOnlyList<RadarTopic> Keep(IReadOnlyList<RadarTopic> topics, IReadOnlyList<Opportunity> existing)
    {
        var ranked = topics.OrderByDescending(topic => topic.Score).ToList();
        return ranked
            .Select((topic, rank) => (Topic: topic, Wanted: topic.ProductIds.Count > 0 && (rank < OpportunityOptions.MaxShown || topic.Watched), Open: MatchOf(topic, existing.Where(o => o.IsOpen)) is not null))
            .Where(entry => entry.Wanted || entry.Open)
            .OrderByDescending(entry => entry.Open || entry.Topic.Watched)
            .ThenByDescending(entry => entry.Topic.Score)
            .Take(OpportunityOptions.MaxKept)
            .OrderByDescending(entry => entry.Topic.Score)
            .Select(entry => entry.Topic)
            .ToList();
    }

    public static MergeResult Merge(IReadOnlyList<Opportunity> existing, IReadOnlyList<RadarTopic> topics, DateTimeOffset now)
    {
        var created = new List<Opportunity>();
        var seen = new HashSet<Guid>();
        var updated = 0;
        foreach (var topic in topics.OrderByDescending(topic => topic.Score))
        {
            var match = MatchOf(topic, existing.Where(o => !seen.Contains(o.Id)));
            if (match is { IsOpen: true })
            {
                seen.Add(match.Id);
                Apply(match, topic, now);
                updated++;
                continue;
            }

            if (match is not null)
            {
                seen.Add(match.Id);
                if (!RoseSharply(match, topic.Demand.Growth))
                {
                    continue;
                }
            }

            if (topic.ProductIds.Count == 0)
            {
                continue;
            }

            var opportunity = new Opportunity { Id = Guid.NewGuid(), FirstSeenAt = now };
            Apply(opportunity, topic, now);
            created.Add(opportunity);
        }

        var expired = 0;
        foreach (var opportunity in existing.Where(o => o.IsOpen && !seen.Contains(o.Id)))
        {
            opportunity.MissedRefreshes++;
            if (opportunity.MissedRefreshes >= OpportunityOptions.ExpireAfterMissed)
            {
                opportunity.Status = OpportunityStatuses.Expired;
                opportunity.ClosedAt = now;
                opportunity.GrowthAtClose = opportunity.Growth;
                expired++;
            }
        }

        return new MergeResult(created, updated, expired);
    }

    public static bool RoseSharply(Opportunity closed, double growth)
    {
        var before = closed.GrowthAtClose ?? closed.Growth;
        return growth >= Math.Max(before * 2, before + SharpRiseGap);
    }

    private static Opportunity? MatchOf(RadarTopic topic, IEnumerable<Opportunity> candidates) =>
        candidates
            .Where(o => TopicText.Similar(o.TopicKey, o.Keywords, topic.Key, [.. topic.Keywords], SameTopic))
            .OrderByDescending(o => o.IsOpen)
            .ThenByDescending(o => o.LastSeenAt)
            .FirstOrDefault();

    private static void Apply(Opportunity opportunity, RadarTopic topic, DateTimeOffset now)
    {
        opportunity.Topic = topic.Name;
        opportunity.TopicKey = topic.Key;
        opportunity.Keywords = [.. topic.Keywords];
        opportunity.Score = topic.Score;
        opportunity.Breakdown = topic.Breakdown;
        opportunity.Growth = topic.Demand.Growth;
        opportunity.Mom = topic.Demand.Mom;
        opportunity.Yoy = topic.Demand.Yoy;
        opportunity.LastMonthSearches = topic.Demand.LastMonth;
        opportunity.AvgMonthlySearches = topic.Demand.Average12;
        opportunity.Trend = topic.Demand.Months.Select(month => new TrendPoint { Year = month.Year, Month = month.Month, Searches = month.Searches }).ToList();
        opportunity.MatchedCategories = [.. topic.Categories];
        if (topic.ProductIds.Count > 0)
        {
            opportunity.MatchedProductIds = [.. topic.ProductIds];
        }

        opportunity.FitReason = topic.FitReason;
        if (topic.Regions is not null)
        {
            opportunity.Regions = topic.Regions;
        }

        if (topic.Reasons is not null && topic.ReasonsStatus is not null)
        {
            opportunity.Reasons = topic.Reasons;
            opportunity.ReasonsStatus = topic.ReasonsStatus;
        }

        opportunity.Model = topic.Model;
        opportunity.PromptVersion = topic.PromptVersion;
        opportunity.LastSeenAt = now;
        opportunity.MissedRefreshes = 0;
    }
}
