using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Refresh;
using Kairo.Modules.Opportunities.Scoring;

namespace Kairo.IntegrationTests.Opportunities;

public sealed class OpportunityScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Growth_compares_last_month_with_the_three_before_it()
    {
        var demand = Series(100, 100, 100, 100, 200);

        Assert.Equal(200, demand.LastMonth);
        Assert.Equal(1.0, demand.Growth);
        Assert.Equal(1.0, demand.Mom);
        Assert.Null(demand.Yoy);
    }

    [Fact]
    public void Year_on_year_needs_thirteen_months()
    {
        var demand = Series([.. Enumerable.Repeat(100L, 12), 150]);

        Assert.Equal(0.5, demand.Yoy);
        Assert.Equal(104, demand.Average12);
    }

    [Fact]
    public void Too_little_history_or_no_earlier_searches_are_handled()
    {
        Assert.Equal(0, Series(10, 20, 30).Growth);
        Assert.Equal(1, Series(0, 0, 0, 0, 50).Growth);
        Assert.Null(Series(0, 50).Mom);
        Assert.Equal(0, new Demand([]).LastMonth);
    }

    [Fact]
    public void Keyword_series_add_up_by_month()
    {
        var sum = Demand.Sum([
            [new MonthlySearches(2026, 8, 10), new MonthlySearches(2026, 9, 20)],
            [new MonthlySearches(2026, 9, 5), new MonthlySearches(2026, 7, 1)],
        ]);

        Assert.Equal([1L, 10, 25], sum.Months.Select(month => month.Searches));
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0.25, 25)]
    [InlineData(3.0, 100)]
    public void Growth_scores_up_to_doubling(double growth, int score) => Assert.Equal(score, OpportunityScoring.GrowthScore(growth));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 40)]
    [InlineData(10000, 80)]
    [InlineData(1000000, 100)]
    public void Volume_scores_on_a_log_scale(long searches, int score) => Assert.Equal(score, OpportunityScoring.VolumeScore(searches));

    [Fact]
    public void The_score_weighs_growth_volume_and_fit_then_feedback()
    {
        var breakdown = new ScoreBreakdown { GrowthScore = 100, VolumeScore = 50, FitScore = 80, Feedback = 0.5 };

        Assert.Equal(40, OpportunityScoring.Score(breakdown));
        Assert.Equal(79, OpportunityScoring.Score(new ScoreBreakdown { GrowthScore = 100, VolumeScore = 50, FitScore = 80 }));
    }

    [Fact]
    public void Dismissals_of_similar_topics_lower_the_score()
    {
        PastDismissal[] dismissals =
        [
            new("monsoon footwear", ["rain boots", "waterproof shoes"], DismissReasons.NotRelevant, Now.AddDays(-10), 5000),
            new("rain boots", ["rain boots"], DismissReasons.AlreadyCovered, Now.AddDays(-20), 5000),
            new("other topic", ["sandals"], DismissReasons.NotRelevant, Now.AddDays(-1), 5000),
        ];

        var (multiplier, reasons) = OpportunityScoring.Feedback("rain shoes", ["rain boots", "gumboots"], 6000, dismissals, Now);

        Assert.Equal(0.35, multiplier);
        Assert.Equal([DismissReasons.NotRelevant, DismissReasons.AlreadyCovered], reasons);
    }

    [Fact]
    public void Old_or_unrelated_feedback_is_ignored()
    {
        PastDismissal[] dismissals =
        [
            new("rain boots", ["rain boots"], DismissReasons.NotRelevant, Now.AddDays(-91), 5000),
            new("rain boots", ["rain boots"], DismissReasons.WrongTiming, Now.AddDays(-31), 5000),
            new("rain boots", ["rain boots"], DismissReasons.TooSmall, Now.AddDays(-5), 5000),
            new("rain boots", ["rain boots"], DismissReasons.Other, Now.AddDays(-5), 5000),
        ];

        var (multiplier, reasons) = OpportunityScoring.Feedback("rain boots", ["rain boots"], 9000, dismissals, Now);

        Assert.Equal(1.0, multiplier);
        Assert.Empty(reasons);
    }

    [Fact]
    public void Too_small_only_lowers_topics_no_bigger_than_the_dismissed_one()
    {
        PastDismissal[] dismissals = [new("rain boots", ["rain boots"], DismissReasons.TooSmall, Now.AddDays(-5), 5000)];

        Assert.Equal(0.8, OpportunityScoring.Feedback("rain boots", ["rain boots"], 4000, dismissals, Now).Multiplier);
        Assert.Equal(1.0, OpportunityScoring.Feedback("rain boots", ["rain boots"], 6000, dismissals, Now).Multiplier);
    }

    [Fact]
    public void Topics_mention_phrases_whatever_the_case_and_spacing()
    {
        Assert.True(TopicText.Mentions(" Rain  Boots ", "Monsoon", ["kids rain boots"]));
        Assert.True(TopicText.Mentions("monsoon", "Monsoon footwear", []));
        Assert.False(TopicText.Mentions("snow", "Monsoon footwear", ["rain boots"]));
        Assert.False(TopicText.Mentions(" ", "Monsoon footwear", []));
    }

    [Fact]
    public void A_closed_opportunity_returns_only_on_a_sharp_rise()
    {
        var dismissed = new Opportunity { Status = OpportunityStatuses.Dismissed, GrowthAtClose = 0.6 };
        var quiet = new Opportunity { Status = OpportunityStatuses.Expired, GrowthAtClose = 0.1 };

        Assert.False(OpportunityMerger.RoseSharply(dismissed, 1.1));
        Assert.True(OpportunityMerger.RoseSharply(dismissed, 1.2));
        Assert.False(OpportunityMerger.RoseSharply(quiet, 0.59));
        Assert.True(OpportunityMerger.RoseSharply(quiet, 0.6));
    }

    [Fact]
    public void Merging_updates_open_opportunities_creates_new_ones_and_expires_the_missing()
    {
        var open = Existing("monsoon footwear", ["rain boots"], OpportunityStatuses.Saved);
        var fading = Existing("winter boots", ["snow boots"], OpportunityStatuses.New, missed: 2);
        RadarTopic[] topics = [Topic("Monsoon footwear", ["rain boots", "gumboots"], 70), Topic("Diwali sandals", ["festive sandals"], 60)];

        var result = OpportunityMerger.Merge([open, fading], topics, Now);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Expired);
        Assert.Equal("Diwali sandals", Assert.Single(result.Created).Topic);
        Assert.Equal((OpportunityStatuses.Saved, 70, 0), (open.Status, open.Score, open.MissedRefreshes));
        Assert.Equal(["rain boots", "gumboots"], open.Keywords);
        Assert.Equal((OpportunityStatuses.Expired, Now), (fading.Status, fading.ClosedAt));
    }

    [Fact]
    public void A_topic_without_products_updates_an_open_opportunity_but_never_creates_one()
    {
        var open = Existing("monsoon footwear", ["rain boots"], OpportunityStatuses.New);
        open.MatchedProductIds = [Guid.NewGuid()];
        var kept = open.MatchedProductIds[0];

        var result = OpportunityMerger.Merge([open], [Topic("Monsoon footwear", ["rain boots"], 50, products: false), Topic("Snow", ["snow"], 90, products: false)], Now);

        Assert.Empty(result.Created);
        Assert.Equal(kept, Assert.Single(open.MatchedProductIds));
    }

    [Fact]
    public void Only_the_top_twelve_and_watched_topics_are_kept()
    {
        var topics = Enumerable.Range(1, 15).Select(i => Topic($"Topic {i}", [$"keyword {i}"], 100 - i)).ToList();
        topics.Add(Topic("Watched", ["watched keyword"], 5, watched: true));

        var kept = OpportunityMerger.Keep(topics, []);

        Assert.Equal(13, kept.Count);
        Assert.Contains(kept, topic => topic.Name == "Watched");
        Assert.DoesNotContain(kept, topic => topic.Name == "Topic 13");
    }

    [Theory]
    [InlineData("2026-10-13T23:29:00Z", "2026-10-13T01:00:00Z", false)]
    [InlineData("2026-10-13T23:30:00Z", "2026-10-13T01:00:00Z", true)]
    [InlineData("2026-10-14T01:00:00Z", "2026-10-14T00:00:00Z", false)]
    [InlineData("2026-10-13T12:00:00Z", null, true)]
    public void A_brand_is_due_once_a_day_from_five_local_time(string now, string? last, bool due) =>
        Assert.Equal(due, RefreshClock.IsDue(DateTimeOffset.Parse(now), "Asia/Kolkata", last is null ? null : DateTimeOffset.Parse(last)));

    private static Demand Series(params long[] searches) =>
        new(searches.Select((value, index) => new MonthlySearches(2025 + ((index + 9) / 12), ((index + 9) % 12) + 1, value)));

    private static Opportunity Existing(string key, List<string> keywords, string status, int missed = 0) => new()
    {
        Id = Guid.NewGuid(),
        Topic = key,
        TopicKey = key,
        Keywords = keywords,
        Status = status,
        MissedRefreshes = missed,
        LastSeenAt = Now.AddDays(-1),
    };

    private static RadarTopic Topic(string name, string[] keywords, int score, bool products = true, bool watched = false) => new()
    {
        Name = name,
        Key = TopicText.Key(name),
        Keywords = keywords,
        Score = score,
        Demand = new Demand([new MonthlySearches(2026, 9, 100)]),
        ProductIds = products ? [Guid.NewGuid()] : [],
        Watched = watched,
    };
}
