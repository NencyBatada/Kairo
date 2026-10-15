using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Contracts.Opportunities;
using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;
using Kairo.Modules.Triggers.Rules;
using Kairo.Modules.Triggers.Weather;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Triggers.Checks;

internal sealed record BrandCheck(Guid BrandId, string Outcome, int Checked, int Fired, int Ended, int Briefs);

internal static class BrandCheckOutcomes
{
    public const string Checked = "checked";
    public const string Error = "error";
}

internal sealed record Firing(string Summary, string Topic, DateTimeOffset EndsAt, object Details);

internal sealed record Evaluation(
    string Outcome,
    IReadOnlyList<string> Regions,
    Func<IReadOnlyList<string>, Firing>? Describe,
    IReadOnlyDictionary<Guid, DateTimeOffset> Extend,
    bool EndActive,
    Guid? ImportId)
{
    public static Evaluation Of(string outcome, Guid? importId = null, bool endActive = false) =>
        new(outcome, [], null, new Dictionary<Guid, DateTimeOffset>(), endActive, importId);
}

internal sealed class BrandChecker(
    TriggersDbContext db,
    KairoDatabase database,
    FestivalCalendar calendar,
    ForecastCache forecasts,
    ICatalogue catalogue,
    IOpportunities opportunities,
    IBrandKit brandKit,
    PlanAgent plan,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    IAgentProgress progress,
    TimeProvider time,
    ILogger<BrandChecker> logger)
{
    public const string Market = "India";
    public const string AuditTarget = "trigger_rule";
    public const int MaxBriefRetries = 3;
    public const int MaxBriefProducts = PlanAgent.MaxBriefProducts;

    public static readonly TimeSpan Hourly = TimeSpan.FromMinutes(55);

    public static string LockName(Guid brandId) => $"triggers:{brandId:N}";

    public async Task<BrandCheck> CheckAsync(BrandSummary brand, bool force, CancellationToken cancellationToken)
    {
        var zone = Conditions.Zone(brand.TimeZone);
        await calendar.EnsureSeededAsync(cancellationToken);
        var ended = await EndFinishedAsync(brand, cancellationToken);
        var rules = await db.BrandRules.AsNoTracking()
            .Where(rule => rule.Status == RuleStatuses.Active)
            .OrderBy(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);
        var active = await db.BrandFirings.AsNoTracking().Where(firing => firing.EndedAt == null).ToListAsync(cancellationToken);
        var latest = rules.Any(rule => rule.Source == TriggerSources.Stock) ? await catalogue.LatestImportAsync(cancellationToken) : null;
        var checkedRules = 0;
        var fired = 0;
        var briefs = 0;
        foreach (var rule in rules)
        {
            var mine = active.Where(firing => firing.RuleId == rule.Id).ToList();
            if (!force && !Due(rule, mine, latest, zone))
            {
                continue;
            }

            checkedRules++;
            Evaluation evaluation;
            try
            {
                evaluation = await EvaluateAsync(rule, mine, latest, zone, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Trigger rule {RuleId} couldn't be checked for brand {BrandId}.", rule.Id, brand.Id);
                evaluation = Evaluation.Of(CheckOutcomes.Failed);
            }

            if (await ApplyAsync(brand, rule, evaluation, cancellationToken) is { } firing)
            {
                fired++;
                if (await DraftAsync(brand, rule, firing, zone, cancellationToken))
                {
                    briefs++;
                }
            }
        }

        briefs += await RetryBriefsAsync(brand, zone, cancellationToken);
        ended += await EndFinishedAsync(brand, cancellationToken);
        return new BrandCheck(brand.Id, BrandCheckOutcomes.Checked, checkedRules, fired, ended, briefs);
    }

    private bool Due(TriggerRule rule, IReadOnlyList<TriggerFiring> mine, CatalogueUpdate? latest, TimeZoneInfo zone)
    {
        if (rule.LastCheckedAt is not { } last)
        {
            return true;
        }

        var now = time.GetUtcNow();
        return rule.Source switch
        {
            TriggerSources.Weather => now - last >= Hourly,
            TriggerSources.Festival => Conditions.Today(last, zone) < Conditions.Today(now, zone),
            _ => (latest is not null && latest.ImportId != rule.LastImportId) || (mine.Count > 0 && now - last >= Hourly),
        };
    }

    private Task<Evaluation> EvaluateAsync(TriggerRule rule, IReadOnlyList<TriggerFiring> mine, CatalogueUpdate? latest, TimeZoneInfo zone, CancellationToken cancellationToken) =>
        rule.Source switch
        {
            TriggerSources.Weather => WeatherAsync(rule, mine, zone, cancellationToken),
            TriggerSources.Festival => FestivalAsync(rule, zone, cancellationToken),
            _ => StockAsync(rule, mine, latest, cancellationToken),
        };

    private async Task<Evaluation> WeatherAsync(TriggerRule rule, IReadOnlyList<TriggerFiring> mine, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var today = Conditions.Today(time.GetUtcNow(), zone);
        var hits = new List<WeatherHit>();
        var available = 0;
        foreach (var regionId in rule.Regions)
        {
            if (IndiaRegions.Find(regionId) is not { HasForecast: true } city
                || await forecasts.DaysAsync(city, cancellationToken) is not { } days)
            {
                continue;
            }

            available++;
            hits.AddRange(Conditions.WeatherHits(rule.Condition, regionId, days, today));
        }

        if (available == 0)
        {
            return Evaluation.Of(CheckOutcomes.WeatherUnavailable);
        }

        var extend = mine
            .Select(firing => (firing.Id, Hits: hits.Where(hit => firing.Regions.Contains(hit.RegionId)).ToList()))
            .Where(entry => entry.Hits.Count > 0)
            .ToDictionary(entry => entry.Id, entry => Conditions.EndOf(entry.Hits.Max(hit => hit.Day), zone));
        if (hits.Count == 0)
        {
            return new Evaluation(CheckOutcomes.Quiet, [], null, extend, false, null);
        }

        var regions = rule.Regions.Where(region => hits.Any(hit => hit.RegionId == region)).ToList();
        return new Evaluation(CheckOutcomes.Fired, regions, chosen =>
        {
            var used = hits.Where(hit => chosen.Contains(hit.RegionId)).ToList();
            var label = rule.Condition.Kind == WeatherKinds.HeavyRain ? "Heavy rain" : "Heatwave";
            return new Firing(
                Conditions.WeatherSummary(rule.Condition, used),
                $"{label} in {RuleText.Joined(chosen)}: {RuleText.Leaf(rule.Category)}",
                Conditions.EndOf(used.Max(hit => hit.Day), zone),
                new { rule.Condition.Kind, rule.Condition.Threshold, hits = used.Select(hit => new { region = hit.RegionId, day = hit.Day, value = hit.Value }) });
        }, extend, false, null);
    }

    private async Task<Evaluation> FestivalAsync(TriggerRule rule, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var today = Conditions.Today(time.GetUtcNow(), zone);
        if (rule.Condition.EventId is not { } eventId
            || await db.BrandFestivals.AsNoTracking().SingleOrDefaultAsync(festival => festival.Id == eventId, cancellationToken) is not { } festival)
        {
            return Evaluation.Of(CheckOutcomes.EventMissing);
        }

        if (today > festival.EndsOn)
        {
            return Evaluation.Of(CheckOutcomes.EventPassed);
        }

        if (!Conditions.FestivalDue(rule.Condition, festival, today))
        {
            return Evaluation.Of(CheckOutcomes.Quiet);
        }

        var regions = rule.Regions.Count > 0 ? rule.Regions : festival.Regions.Count > 0 ? festival.Regions : [IndiaRegions.CountryId];
        return new Evaluation(CheckOutcomes.Fired, regions, chosen => new Firing(
            Conditions.FestivalSummary(festival, today),
            $"{festival.Name}: {RuleText.Leaf(rule.Category)}",
            Conditions.EndOf(festival.EndsOn, zone),
            new { eventId = festival.Id, festival.Name, festival.StartsOn, festival.EndsOn }), new Dictionary<Guid, DateTimeOffset>(), false, null);
    }

    private async Task<Evaluation> StockAsync(TriggerRule rule, IReadOnlyList<TriggerFiring> mine, CatalogueUpdate? latest, CancellationToken cancellationToken)
    {
        var importId = latest?.ImportId;
        var stock = await catalogue.GetCategoryStockAsync(rule.Category, cancellationToken);
        if (stock.InStockProducts == 0)
        {
            return Evaluation.Of(CheckOutcomes.NoStock, importId, endActive: true);
        }

        if (stock.ProductsWithQuantity == 0)
        {
            return Evaluation.Of(CheckOutcomes.NeedsQuantities, importId, endActive: true);
        }

        if (stock.Units < (rule.Condition.MinUnits ?? long.MaxValue))
        {
            return Evaluation.Of(CheckOutcomes.Quiet, importId, endActive: true);
        }

        var until = time.GetUtcNow() + Conditions.StockHold;
        var extend = mine.ToDictionary(firing => firing.Id, _ => until);
        var regions = rule.Regions.Count > 0 ? rule.Regions : [IndiaRegions.CountryId];
        var leaf = RuleText.Leaf(rule.Category);
        var products = stock.ProductsWithQuantity == 1 ? "1 product" : $"{stock.ProductsWithQuantity} products";
        return new Evaluation(CheckOutcomes.Fired, regions, chosen => new Firing(
            $"{leaf} has {RuleText.Number(stock.Units)} units in stock across {products}.",
            $"Clear {leaf} stock",
            until,
            new { stock.Units, stock.ProductsWithQuantity, stock.InStockProducts, rule.Condition.MinUnits }), extend, false, importId);
    }

    private async Task<TriggerFiring?> ApplyAsync(BrandSummary brand, TriggerRule rule, Evaluation evaluation, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(brand.Id), cancellationToken);
            var tracked = await db.Rules.SingleOrDefaultAsync(r => r.Id == rule.Id, cancellationToken);
            if (tracked is not { Status: RuleStatuses.Active })
            {
                await database.RollbackAsync(cancellationToken);
                return null;
            }

            var now = time.GetUtcNow();
            var firings = await db.BrandFirings.Where(f => f.RuleId == rule.Id && (f.EndedAt == null || f.CooldownUntil > now)).ToListAsync(cancellationToken);
            foreach (var current in firings.Where(f => f.EndedAt is null))
            {
                if (evaluation.EndActive)
                {
                    current.ConditionEndsAt = current.ConditionEndsAt < now ? current.ConditionEndsAt : now;
                }
                else if (evaluation.Extend.TryGetValue(current.Id, out var until) && until > current.ConditionEndsAt)
                {
                    current.ConditionEndsAt = until;
                    if (current.OpportunityId is { } opportunityId)
                    {
                        await opportunities.ExtendTriggerAsync(opportunityId, current.Id, until, cancellationToken);
                    }
                }
            }

            var outcome = evaluation.Outcome;
            TriggerFiring? firing = null;
            if (outcome == CheckOutcomes.Fired && evaluation.Describe is { } describe)
            {
                var free = evaluation.Regions.Where(region => !firings.Any(f => f.Blocks(region, now))).ToList();
                if (free.Count == 0)
                {
                    outcome = CheckOutcomes.CoolingDown;
                }
                else
                {
                    var described = describe(free);
                    var candidate = new TriggerFiring
                    {
                        Id = Guid.NewGuid(),
                        RuleId = rule.Id,
                        Regions = free,
                        FiredAt = now,
                        CooldownUntil = now.AddHours(tracked.CooldownHours),
                        ConditionEndsAt = described.EndsAt,
                        Summary = described.Summary.Length > 500 ? described.Summary[..500] : described.Summary,
                        Details = JsonSerializer.Serialize(described.Details, JsonSerializerOptions.Web),
                    };
                    var raised = await opportunities.RaiseTriggeredAsync(
                        new TriggerSignal(
                            candidate.Id,
                            rule.Id,
                            tracked.Name,
                            tracked.Source,
                            described.Topic,
                            candidate.Summary,
                            tracked.Category,
                            free.Select(region => new TriggerRegion(region, IndiaRegions.NameOf(region))).ToList(),
                            now,
                            candidate.ConditionEndsAt),
                        cancellationToken);
                    if (raised.Outcome == TriggerOutcomes.NoStock || raised.OpportunityId is null)
                    {
                        outcome = CheckOutcomes.NoStock;
                    }
                    else
                    {
                        candidate.OpportunityId = raised.OpportunityId;
                        candidate.Outcome = raised.Outcome == TriggerOutcomes.Joined ? FiringOutcomes.Joined : FiringOutcomes.Created;
                        db.Firings.Add(candidate);
                        tracked.LastFiredAt = now;
                        firing = candidate;
                    }
                }
            }

            var changed = firing is not null || tracked.LastCheckOutcome != outcome;
            tracked.LastCheckedAt = now;
            tracked.LastCheckOutcome = outcome;
            if (evaluation.ImportId is { } importId)
            {
                tracked.LastImportId = importId;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (firing is not null)
            {
                await audit.RecordAsync(
                    new AuditRecord(
                        "trigger.fired",
                        AuditTarget,
                        rule.Id.ToString(),
                        After: new { firingId = firing.Id, firing.Regions, firing.Summary, firing.Outcome, firing.OpportunityId },
                        Actor: AuditActor.Agent("Sense agent", "sense")),
                    cancellationToken);
                await notifications.NotifyAsync(Fired(brand, tracked, firing), cancellationToken);
            }

            if (changed)
            {
                await live.PublishAsync(TriggerLiveEvents.RulesChanged(), cancellationToken);
            }

            await database.CommitAsync(cancellationToken);
            return firing;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> EndFinishedAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (!await db.BrandFirings.AnyAsync(firing => firing.EndedAt == null && firing.ConditionEndsAt <= now, cancellationToken))
        {
            return 0;
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(brand.Id), cancellationToken);
            var finished = await db.BrandFirings.Where(firing => firing.EndedAt == null && firing.ConditionEndsAt <= now).ToListAsync(cancellationToken);
            foreach (var firing in finished)
            {
                firing.EndedAt = now;
                if (firing.OpportunityId is { } opportunityId)
                {
                    await opportunities.EndTriggerAsync(opportunityId, firing.Id, cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            if (finished.Count > 0)
            {
                await live.PublishAsync(TriggerLiveEvents.RulesChanged(), cancellationToken);
            }

            await database.CommitAsync(cancellationToken);
            return finished.Count;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> RetryBriefsAsync(BrandSummary brand, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var active = await db.BrandFirings.AsNoTracking()
            .Where(firing => firing.EndedAt == null && firing.OpportunityId != null)
            .OrderByDescending(firing => firing.FiredAt)
            .ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return 0;
        }

        var states = await opportunities.GetTriggeredAsync(active.Select(firing => firing.OpportunityId!.Value).ToHashSet(), cancellationToken);
        var failed = states
            .Where(state => state.BriefStatus == "failed" && state.Status is "new" or "saved")
            .Select(state => state.Id)
            .ToHashSet();
        var retries = active.Where(firing => failed.Contains(firing.OpportunityId!.Value)).DistinctBy(firing => firing.OpportunityId).Take(MaxBriefRetries).ToList();
        var ruleIds = retries.Select(firing => firing.RuleId).ToList();
        var rules = await db.Rules.AsNoTracking().Where(rule => ruleIds.Contains(rule.Id)).ToDictionaryAsync(rule => rule.Id, cancellationToken);
        var drafted = 0;
        foreach (var firing in retries.Where(firing => rules.ContainsKey(firing.RuleId)))
        {
            if (await DraftAsync(brand, rules[firing.RuleId], firing, zone, cancellationToken))
            {
                drafted++;
            }
        }

        return drafted;
    }

    private async Task<bool> DraftAsync(BrandSummary brand, TriggerRule rule, TriggerFiring firing, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (firing.OpportunityId is not { } opportunityId
            || (await opportunities.GetTriggeredAsync([opportunityId], cancellationToken)).FirstOrDefault() is not { Status: "new" or "saved" } state)
        {
            return false;
        }

        var subject = new AgentSubject(AgentSubjectKind.Opportunity, opportunityId);
        await progress.ReportAsync(new AgentProgress(firing.Id, AgentName.Plan, AgentJobStatus.Running, "Drafting the brief") { Subject = subject }, cancellationToken);
        TriggerBrief? brief = null;
        try
        {
            var products = (await catalogue.GetInStockAsync(state.ProductIds, cancellationToken)).Take(MaxBriefProducts).ToList();
            var keyed = products.Select((product, index) => (Key: PlanAgent.ProductKey(index), Product: product)).ToList();
            var regions = firing.Regions.Select(IndiaRegions.NameOf).ToList();
            var kit = await brandKit.GetApprovedAsync(cancellationToken);
            var answer = await plan.DraftAsync(
                new TriggerBriefRequest(
                    brand.Name,
                    Market,
                    rule.Category,
                    new BriefTrigger(rule.Source, firing.Summary, regions, Conditions.Date(Conditions.Today(firing.ConditionEndsAt.AddTicks(-1), zone))),
                    keyed.Select(entry => new BriefProduct(entry.Key, entry.Product.Title, entry.Product.SalePrice ?? entry.Product.Price, entry.Product.Currency)).ToList(),
                    kit?.Content),
                cancellationToken);
            brief = BriefOf(answer, keyed.Select(entry => (entry.Key, entry.Product.Id)).ToList(), regions, time.GetUtcNow());
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "The Plan agent couldn't draft a brief for opportunity {OpportunityId}.", opportunityId);
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await opportunities.SaveBriefAsync(opportunityId, brief, cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }

        await progress.ReportAsync(
            new AgentProgress(firing.Id, AgentName.Plan, brief is null ? AgentJobStatus.Failed : AgentJobStatus.Succeeded, brief is null ? "Brief not drafted" : "Brief drafted") { Subject = subject },
            cancellationToken);
        return brief is not null;
    }

    public static TriggerBrief? BriefOf(TriggerBriefAnswer answer, IReadOnlyList<(string Key, Guid Id)> products, IReadOnlyList<string> regions, DateTimeOffset now) =>
        PlanAgent.Clean(answer, products, regions) is { } clean
            ? new TriggerBrief(
                clean.Title,
                clean.Goal,
                clean.Audience,
                clean.KeyMessage,
                clean.Offer,
                clean.Regions,
                clean.ProductIds,
                clean.Channels,
                clean.Timing,
                answer.Model,
                answer.PromptVersion,
                now)
            : null;

    private static Notification Fired(BrandSummary brand, TriggerRule rule, TriggerFiring firing)
    {
        var next = firing.Outcome == FiringOutcomes.Joined
            ? "Kairo added it to an open opportunity."
            : "Kairo drafted an opportunity for you to review.";
        return new Notification(
            "trigger.fired",
            LiveAudience.Roles(BrandRoles.Marketer),
            $"Trigger fired: {rule.Name}",
            $"{firing.Summary} {next}",
            $"/{brand.Handle}/opportunities/{firing.OpportunityId}");
    }
}
