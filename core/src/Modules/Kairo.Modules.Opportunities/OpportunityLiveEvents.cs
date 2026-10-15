using Kairo.Contracts.Live;
using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Refresh;

namespace Kairo.Modules.Opportunities;

internal static class OpportunityLiveEvents
{
    public static LiveEvent Refreshed(RefreshRun run) =>
        new("opportunities.refreshed", new { runId = run.Id, created = run.Created, updated = run.Updated, expired = run.Expired }, LiveAudience.Everyone);

    public static LiveEvent Changed(Opportunity opportunity) =>
        new("opportunity.changed", new { opportunityId = opportunity.Id, status = opportunity.Status }, LiveAudience.Everyone);

    public static LiveEvent TopicsChanged() =>
        new("opportunity_topics.changed", new { }, LiveAudience.Everyone);
}
