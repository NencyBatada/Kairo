using Kairo.Contracts.Audit;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Live;
using Kairo.Contracts.Opportunities;
using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;

namespace Kairo.Modules.Triggers.Rules;

internal static class RuleEndpoints
{
    public const string AuditTarget = "trigger_rule";
    public const int RecentFirings = 20;

    public static async Task<Ok<TriggerOptions>> OptionsAsync(ICatalogue catalogue, CancellationToken cancellationToken)
    {
        var categories = await catalogue.ListInStockCategoriesAsync(cancellationToken);
        return TypedResults.Ok(new TriggerOptions(
            IndiaRegions.All.Select(PlaceView.Of).ToArray(),
            categories.Select(category => category.Name).ToArray(),
            [
                new WeatherKindView(WeatherKinds.HeavyRain, "mm", RuleLimits.DefaultRainMillimetres, RuleLimits.MinRainMillimetres, RuleLimits.MaxRainMillimetres),
                new WeatherKindView(WeatherKinds.Heatwave, "°C", RuleLimits.DefaultHeatCelsius, RuleLimits.MinHeatCelsius, RuleLimits.MaxHeatCelsius),
            ],
            RuleLimits.DefaultWithinDays,
            RuleLimits.MaxWithinDays,
            RuleLimits.DefaultDaysBefore,
            RuleLimits.MaxDaysBefore,
            RuleLimits.DefaultCooldownHours,
            RuleLimits.MaxCooldownHours,
            RuleLimits.MaxRegions));
    }

    public static async Task<Results<Ok<RulePreview>, ValidationProblem>> PreviewAsync(
        RuleRequest request,
        TriggersDbContext db,
        CancellationToken cancellationToken)
    {
        var (draft, errors) = await RuleInput.CheckAsync(request, db, cancellationToken);
        if (draft is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(new RulePreview(RuleText.Describe(draft.Source, draft.Condition, draft.Regions, draft.Category, draft.CooldownHours, draft.Event)));
    }

    public static async Task<Results<Ok<RuleView[]>, ValidationProblem>> ListAsync(
        string? status,
        TriggersDbContext db,
        IOpportunities opportunities,
        CancellationToken cancellationToken)
    {
        if (status is not null && !RuleStatuses.Listed.Contains(status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use draft, active or paused."] });
        }

        var rules = await db.BrandRules.AsNoTracking()
            .Where(rule => status == null || rule.Status == status)
            .OrderBy(rule => rule.Category)
            .ThenBy(rule => rule.Name)
            .ToListAsync(cancellationToken);
        var stats = await RuleStats.LoadAsync(db, opportunities, rules.Select(rule => rule.Id).ToList(), cancellationToken);
        var festivals = await FestivalsAsync(db, rules, cancellationToken);
        return TypedResults.Ok(rules.Select(rule => RuleView.Of(rule, EventOf(rule, festivals), stats.ResultsOf(rule.Id))).ToArray());
    }

    public static async Task<Results<Ok<RuleDetail>, NotFound>> GetAsync(
        Guid ruleId,
        TriggersDbContext db,
        IOpportunities opportunities,
        CancellationToken cancellationToken)
    {
        if (await db.BrandRules.AsNoTracking().SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await DetailAsync(rule, db, opportunities, cancellationToken));
    }

    public static async Task<Results<Created<RuleDetail>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        RuleRequest request,
        TriggersDbContext db,
        KairoDatabase database,
        IOpportunities opportunities,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (draft, errors) = await RuleInput.CheckAsync(request, db, cancellationToken);
        if (draft is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await LimitReachedAsync(db, database, member.BrandId, cancellationToken))
        {
            return TooMany();
        }

        var now = time.GetUtcNow();
        var rule = new TriggerRule
        {
            Id = Guid.NewGuid(),
            Status = RuleStatuses.Active,
            Origin = RuleOrigins.Manual,
            CreatedBy = member.UserId,
            CreatedAt = now,
            UpdatedBy = member.UserId,
            UpdatedAt = now,
        };
        draft.ApplyTo(rule);
        db.Rules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        var detail = await DetailAsync(rule, db, opportunities, cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.created", AuditTarget, rule.Id.ToString(), After: Summary(detail.Rule)), audit, live, cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/triggers/rules/{rule.Id}", detail);
    }

    public static async Task<Results<Ok<RuleDetail>, NotFound, ValidationProblem>> UpdateAsync(
        Guid ruleId,
        RuleRequest request,
        TriggersDbContext db,
        IOpportunities opportunities,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (draft, errors) = await RuleInput.CheckAsync(request, db, cancellationToken);
        if (draft is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.BrandRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        var before = Summary((await DetailAsync(rule, db, opportunities, cancellationToken)).Rule);
        draft.ApplyTo(rule);
        rule.UpdatedBy = member.UserId;
        rule.UpdatedAt = time.GetUtcNow();
        rule.LastCheckedAt = null;
        rule.LastCheckOutcome = null;
        rule.LastImportId = null;
        await db.SaveChangesAsync(cancellationToken);
        var detail = await DetailAsync(rule, db, opportunities, cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.updated", AuditTarget, rule.Id.ToString(), before, Summary(detail.Rule)), audit, live, cancellationToken);
        return TypedResults.Ok(detail);
    }

    public static async Task<Results<Ok<RuleDetail>, NotFound, ProblemHttpResult>> PauseAsync(
        Guid ruleId,
        TriggersDbContext db,
        IOpportunities opportunities,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.BrandRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        if (rule.Status != RuleStatuses.Active)
        {
            return Conflict(rule);
        }

        var detail = await MoveAsync(rule, RuleStatuses.Paused, db, opportunities, member, time, cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.paused", AuditTarget, rule.Id.ToString(), new { Status = RuleStatuses.Active, rule.Name }, new { rule.Status, rule.Name }), audit, live, cancellationToken);
        return TypedResults.Ok(detail);
    }

    public static async Task<Results<Ok<RuleDetail>, NotFound, ProblemHttpResult>> ResumeAsync(
        Guid ruleId,
        TriggersDbContext db,
        IOpportunities opportunities,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.BrandRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        if (rule.Status != RuleStatuses.Paused)
        {
            return Conflict(rule);
        }

        var detail = await MoveAsync(rule, RuleStatuses.Active, db, opportunities, member, time, cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.resumed", AuditTarget, rule.Id.ToString(), new { Status = RuleStatuses.Paused, rule.Name }, new { rule.Status, rule.Name }), audit, live, cancellationToken);
        return TypedResults.Ok(detail);
    }

    public static async Task<Results<Ok<RuleDetail>, NotFound, ProblemHttpResult>> ActivateAsync(
        Guid ruleId,
        TriggersDbContext db,
        IOpportunities opportunities,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.BrandRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        if (rule.Status != RuleStatuses.Draft)
        {
            return Conflict(rule);
        }

        var detail = await MoveAsync(rule, RuleStatuses.Active, db, opportunities, member, time, cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.activated", AuditTarget, rule.Id.ToString(), new { Status = RuleStatuses.Draft, rule.Name }, new { rule.Status, rule.Name }), audit, live, cancellationToken);
        return TypedResults.Ok(detail);
    }

    public static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid ruleId,
        TriggersDbContext db,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.BrandRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) is not { } rule)
        {
            return TypedResults.NotFound();
        }

        var before = rule.Status;
        rule.Status = RuleStatuses.Deleted;
        rule.UpdatedBy = member.UserId;
        rule.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(new AuditRecord("trigger_rule.deleted", AuditTarget, rule.Id.ToString(), Before: new { Status = before, rule.Name }), audit, live, cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<bool> LimitReachedAsync(TriggersDbContext db, KairoDatabase database, Guid brandId, CancellationToken cancellationToken)
    {
        await database.LockAsync($"trigger_rules:{brandId:N}", cancellationToken);
        return await db.BrandRules.CountAsync(cancellationToken) >= RuleLimits.MaxRules;
    }

    public static ProblemHttpResult TooMany() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"A workspace can have up to {RuleLimits.MaxRules} trigger rules.");

    private static async Task<RuleDetail> MoveAsync(
        TriggerRule rule,
        string status,
        TriggersDbContext db,
        IOpportunities opportunities,
        CurrentMember member,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        rule.Status = status;
        rule.UpdatedBy = member.UserId;
        rule.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(rule, db, opportunities, cancellationToken);
    }

    private static async Task<RuleDetail> DetailAsync(TriggerRule rule, TriggersDbContext db, IOpportunities opportunities, CancellationToken cancellationToken)
    {
        var stats = await RuleStats.LoadAsync(db, opportunities, [rule.Id], cancellationToken);
        var festivals = await FestivalsAsync(db, [rule], cancellationToken);
        var firings = stats.FiringsOf(rule.Id).Take(RecentFirings).Select(firing => FiringView.Of(firing, stats.StatusOf(firing.OpportunityId))).ToArray();
        return new RuleDetail(RuleView.Of(rule, EventOf(rule, festivals), stats.ResultsOf(rule.Id)), firings);
    }

    private static async Task<Dictionary<Guid, FestivalEvent>> FestivalsAsync(TriggersDbContext db, IReadOnlyList<TriggerRule> rules, CancellationToken cancellationToken)
    {
        var ids = rules.Select(rule => rule.Condition.EventId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.BrandFestivals.AsNoTracking().Where(festival => ids.Contains(festival.Id)).ToDictionaryAsync(festival => festival.Id, cancellationToken);
    }

    private static FestivalEvent? EventOf(TriggerRule rule, Dictionary<Guid, FestivalEvent> festivals) =>
        rule.Condition.EventId is { } id ? festivals.GetValueOrDefault(id) : null;

    private static object Summary(RuleView rule) => new { rule.Name, rule.Source, rule.Status, rule.Description };

    private static ProblemHttpResult Conflict(TriggerRule rule) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"This rule is {rule.Status}.");

    private static async Task RecordAsync(AuditRecord record, IAuditLog audit, ILiveUpdates live, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(record, cancellationToken);
        await live.PublishAsync(TriggerLiveEvents.RulesChanged(), cancellationToken);
    }
}

internal sealed class RuleStats(IReadOnlyList<TriggerFiring> firings, IReadOnlyDictionary<Guid, string> statuses)
{
    public static async Task<RuleStats> LoadAsync(TriggersDbContext db, IOpportunities opportunities, IReadOnlyCollection<Guid> ruleIds, CancellationToken cancellationToken)
    {
        if (ruleIds.Count == 0)
        {
            return new RuleStats([], new Dictionary<Guid, string>());
        }

        var ids = ruleIds.ToList();
        var firings = await db.BrandFirings.AsNoTracking()
            .Where(firing => ids.Contains(firing.RuleId))
            .OrderByDescending(firing => firing.FiredAt)
            .ToListAsync(cancellationToken);
        var states = await opportunities.GetTriggeredAsync(firings.Select(firing => firing.OpportunityId).OfType<Guid>().ToHashSet(), cancellationToken);
        return new RuleStats(firings, states.ToDictionary(state => state.Id, state => state.Status));
    }

    public IEnumerable<TriggerFiring> FiringsOf(Guid ruleId) => firings.Where(firing => firing.RuleId == ruleId);

    public string? StatusOf(Guid? opportunityId) => opportunityId is { } id ? statuses.GetValueOrDefault(id) : null;

    public RuleResults ResultsOf(Guid ruleId)
    {
        var own = FiringsOf(ruleId).ToList();
        var accepted = own.Select(firing => firing.OpportunityId).OfType<Guid>().Distinct().Count(id => statuses.GetValueOrDefault(id) == "accepted");
        return new RuleResults(
            own.Count,
            own.Count(firing => firing.Outcome == FiringOutcomes.Created),
            own.Count(firing => firing.Outcome == FiringOutcomes.Joined),
            accepted,
            null);
    }
}
