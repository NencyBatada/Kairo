using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Live;
using Kairo.Contracts.Opportunities;
using Kairo.Modules.Opportunities.Data;
using Kairo.Modules.Opportunities.Radar;

namespace Kairo.Modules.Opportunities.Triggered;

internal sealed class TriggeredOpportunities(
    OpportunitiesDbContext db,
    KairoDatabase database,
    ICatalogue catalogue,
    ILiveUpdates live,
    TimeProvider time) : IOpportunities
{
    public const string KeyPrefix = "trigger:";
    public const int MaxTopicLength = 200;
    public const int MaxSummaryLength = 500;

    public async Task<TriggerOutcome> RaiseTriggeredAsync(TriggerSignal signal, CancellationToken cancellationToken)
    {
        var products = await catalogue.FindInStockAsync([signal.Category], [signal.Topic], OpportunityOptions.MaxProducts, cancellationToken);
        if (products.Count == 0)
        {
            return new TriggerOutcome(TriggerOutcomes.NoStock, null, null, []);
        }

        var productIds = products.Select(product => product.Id).ToList();
        var trigger = TriggerOf(signal);
        if (await OverlappingAsync(signal.Category, productIds, cancellationToken) is { } joined)
        {
            joined.Triggers.RemoveAll(existing => existing.FiringId == signal.FiringId);
            joined.Triggers.Add(trigger);
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(OpportunityLiveEvents.Changed(joined), cancellationToken);
            return new TriggerOutcome(TriggerOutcomes.Joined, joined.Id, joined.Topic, [.. joined.MatchedProductIds]);
        }

        var opportunity = new Opportunity
        {
            Id = Guid.NewGuid(),
            Topic = Cut(signal.Topic, MaxTopicLength),
            TopicKey = $"{KeyPrefix}{signal.FiringId:N}",
            Source = OpportunitySources.Trigger,
            MatchedCategories = [signal.Category],
            MatchedProductIds = productIds,
            FitReason = Cut(signal.Summary, MaxSummaryLength),
            ReasonsStatus = ReasonsStatuses.Trigger,
            FirstSeenAt = signal.FiredAt,
            LastSeenAt = signal.FiredAt,
            Triggers = [trigger],
        };
        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.Changed(opportunity), cancellationToken);
        return new TriggerOutcome(TriggerOutcomes.Created, opportunity.Id, opportunity.Topic, productIds);
    }

    public async Task SaveBriefAsync(Guid opportunityId, TriggerBrief? brief, CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, cancellationToken) is not { IsOpen: true } opportunity)
        {
            return;
        }

        if (brief is null)
        {
            opportunity.BriefStatus = BriefStatuses.Failed;
        }
        else
        {
            opportunity.Brief = new OpportunityBrief
            {
                Title = brief.Title,
                Goal = brief.Goal,
                Audience = brief.Audience,
                KeyMessage = brief.KeyMessage,
                Offer = brief.Offer,
                Regions = [.. brief.Regions],
                ProductIds = [.. brief.ProductIds],
                Channels = [.. brief.Channels],
                Timing = brief.Timing,
                Model = brief.Model,
                PromptVersion = brief.PromptVersion,
                DraftedAt = brief.DraftedAt,
            };
            opportunity.BriefStatus = BriefStatuses.Drafted;
        }

        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.Changed(opportunity), cancellationToken);
    }

    public async Task ExtendTriggerAsync(Guid opportunityId, Guid firingId, DateTimeOffset conditionEndsAt, CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, cancellationToken) is not { } opportunity
            || opportunity.Triggers.FirstOrDefault(trigger => trigger.FiringId == firingId) is not { Ended: false } trigger
            || trigger.ConditionEndsAt == conditionEndsAt)
        {
            return;
        }

        trigger.ConditionEndsAt = conditionEndsAt;
        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.Changed(opportunity), cancellationToken);
    }

    public async Task EndTriggerAsync(Guid opportunityId, Guid firingId, CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, cancellationToken) is not { } opportunity
            || opportunity.Triggers.FirstOrDefault(trigger => trigger.FiringId == firingId) is not { Ended: false } trigger)
        {
            return;
        }

        trigger.Ended = true;
        if (opportunity.Source == OpportunitySources.Trigger && opportunity.IsOpen && opportunity.ActiveTrigger is null)
        {
            opportunity.Status = OpportunityStatuses.Expired;
            opportunity.ClosedAt = time.GetUtcNow();
            opportunity.GrowthAtClose = opportunity.Growth;
        }

        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.Changed(opportunity), cancellationToken);
    }

    public async Task<IReadOnlyList<TriggeredState>> GetTriggeredAsync(IReadOnlyCollection<Guid> opportunityIds, CancellationToken cancellationToken)
    {
        if (opportunityIds.Count == 0)
        {
            return [];
        }

        var ids = opportunityIds.Distinct().ToList();
        var opportunities = await db.BrandOpportunities.AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        return opportunities.Select(o => new TriggeredState(o.Id, o.Topic, o.Status, o.BriefStatus, [.. o.MatchedProductIds])).ToList();
    }

    private async Task<Opportunity?> OverlappingAsync(string category, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        var open = await db.BrandOpportunities.AsNoTracking()
            .Where(o => o.Status == OpportunityStatuses.New || o.Status == OpportunityStatuses.Saved)
            .ToListAsync(cancellationToken);
        var candidates = open
            .Where(o => o.MatchedCategories.Contains(category, StringComparer.Ordinal) || o.MatchedProductIds.Any(productIds.Contains))
            .OrderByDescending(o => o.ActiveTrigger is not null)
            .ThenByDescending(o => o.Score)
            .ThenByDescending(o => o.LastSeenAt)
            .Select(o => o.Id)
            .ToList();
        foreach (var id in candidates)
        {
            if (await LockedAsync(id, cancellationToken) is { IsOpen: true } locked)
            {
                return locked;
            }
        }

        return null;
    }

    private async Task<Opportunity?> LockedAsync(Guid opportunityId, CancellationToken cancellationToken)
    {
        await database.LockAsync($"opportunity:{opportunityId:N}", cancellationToken);
        return await db.BrandOpportunities.SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);
    }

    private static OpportunityTrigger TriggerOf(TriggerSignal signal) => new()
    {
        FiringId = signal.FiringId,
        RuleId = signal.RuleId,
        RuleName = signal.RuleName,
        Source = signal.Source,
        Summary = Cut(signal.Summary, MaxSummaryLength),
        Regions = signal.Regions.Select(region => new TriggerPlace { Id = region.Id, Name = region.Name }).ToList(),
        FiredAt = signal.FiredAt,
        ConditionEndsAt = signal.ConditionEndsAt,
    };

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
