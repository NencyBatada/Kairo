using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;

namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedNotices
{
    public const string AuditTarget = "feed_run";

    private const int MaxDetailLength = 600;

    public static string NameOf(string feed) => feed switch
    {
        FeedNames.GoogleAds => "Google Ads",
        FeedNames.Ga4 => "Google Analytics",
        FeedNames.SalesTable => "Sales table",
        _ => feed,
    };

    public static LiveEvent Event(FeedRun run) =>
        new LiveEvent("feeds.changed", new { feed = run.Feed, status = run.Status }, LiveAudience.Everyone);

    public static AuditRecord Started(FeedRun run) =>
        new AuditRecord("analytics.feed_started", AuditTarget, run.Id.ToString(), After: Details(run));

    public static AuditRecord Succeeded(FeedRun run) =>
        new AuditRecord("analytics.feed_succeeded", AuditTarget, run.Id.ToString(), After: Details(run));

    public static AuditRecord Failed(FeedRun run) =>
        new AuditRecord("analytics.feed_failed", AuditTarget, run.Id.ToString(), After: Details(run));

    private static object Details(FeedRun run) =>
        new
        {
            run.Feed,
            run.ExternalId,
            run.Status,
            run.Reason,
            run.FromDate,
            run.ToDate,
            run.Rows,
            run.Error,
        };

    public static Notification FeedFailed(FeedRun run)
    {
        var name = NameOf(run.Feed);
        var detail = run.Error is { Length: > MaxDetailLength } error ? error[..MaxDetailLength] : run.Error;
        return new Notification(
            "connection.problem",
            LiveAudience.Roles(BrandRoles.Admin),
            $"{name} data didn't refresh",
            $"Kairo couldn't refresh the {name} data, so reports may be out of date. It will try again in the next nightly refresh."
                + (detail is null ? "" : $" Details: {detail}"),
            "/settings/connections");
    }
}
