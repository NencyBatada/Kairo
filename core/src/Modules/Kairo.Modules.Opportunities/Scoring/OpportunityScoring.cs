using Kairo.Modules.Opportunities.Radar;

namespace Kairo.Modules.Opportunities.Scoring;

internal sealed record PastDismissal(string TopicKey, IReadOnlyCollection<string> Keywords, string Reason, DateTimeOffset At, long LastMonthSearches);

internal static class OpportunityScoring
{
    public const double GrowthWeight = 0.4;
    public const double VolumeWeight = 0.3;
    public const double FitWeight = 0.3;
    public const double FullGrowth = 1.0;
    public const double FullVolumeDigits = 5;
    public const double SimilarTopics = 0.3;
    public const double LowestFeedback = 0.3;

    public static readonly TimeSpan FeedbackWindow = TimeSpan.FromDays(90);
    public static readonly TimeSpan TimingWindow = TimeSpan.FromDays(30);

    public static int GrowthScore(double growth) => Clamp(growth / FullGrowth * 100);

    public static int VolumeScore(long searches) => searches <= 0 ? 0 : Clamp(Math.Log10(searches) / FullVolumeDigits * 100);

    public static int FitScore(double fit) => Clamp(fit * 100);

    public static ScoreBreakdown Breakdown(Demand demand, double fit, IReadOnlyCollection<string> keywords, string topicKey, IEnumerable<PastDismissal> dismissals, DateTimeOffset now)
    {
        var (feedback, reasons) = Feedback(topicKey, keywords, demand.LastMonth, dismissals, now);
        return new ScoreBreakdown
        {
            GrowthScore = GrowthScore(demand.Growth),
            VolumeScore = VolumeScore(demand.LastMonth),
            FitScore = FitScore(fit),
            Fit = Math.Round(Math.Clamp(fit, 0, 1), 2),
            Feedback = feedback,
            FeedbackReasons = reasons,
        };
    }

    public static int Score(ScoreBreakdown breakdown) =>
        Clamp(((GrowthWeight * breakdown.GrowthScore) + (VolumeWeight * breakdown.VolumeScore) + (FitWeight * breakdown.FitScore)) * breakdown.Feedback);

    public static (double Multiplier, List<string> Reasons) Feedback(
        string topicKey,
        IReadOnlyCollection<string> keywords,
        long lastMonthSearches,
        IEnumerable<PastDismissal> dismissals,
        DateTimeOffset now)
    {
        var multiplier = 1.0;
        var reasons = new List<string>();
        foreach (var dismissal in dismissals.Where(d => now - d.At <= FeedbackWindow).OrderByDescending(d => d.At))
        {
            if (reasons.Contains(dismissal.Reason) || !TopicText.Similar(topicKey, keywords, dismissal.TopicKey, dismissal.Keywords, SimilarTopics))
            {
                continue;
            }

            var factor = dismissal.Reason switch
            {
                DismissReasons.NotRelevant => 0.5,
                DismissReasons.AlreadyCovered => 0.7,
                DismissReasons.WrongTiming when now - dismissal.At <= TimingWindow => 0.9,
                DismissReasons.TooSmall when lastMonthSearches <= dismissal.LastMonthSearches => 0.8,
                _ => 1.0,
            };
            if (factor < 1)
            {
                multiplier *= factor;
                reasons.Add(dismissal.Reason);
            }
        }

        return (Math.Round(Math.Max(multiplier, LowestFeedback), 2), reasons);
    }

    private static int Clamp(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
}
