using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Regions;
using Kairo.Modules.Triggers.Rules;

namespace Kairo.Modules.Triggers.Festivals;

internal sealed record FestivalRequest(string? Name, DateOnly? StartsOn, DateOnly? EndsOn, string[]? Regions);

internal sealed record FestivalView(Guid Id, string Name, DateOnly StartsOn, DateOnly EndsOn, PlaceView[] Regions, string Origin, int Rules)
{
    public static FestivalView Of(FestivalEvent festival, int rules) => new(
        festival.Id,
        festival.Name,
        festival.StartsOn,
        festival.EndsOn,
        festival.Regions.Select(PlaceView.Of).ToArray(),
        festival.Origin,
        rules);
}

internal static class FestivalEndpoints
{
    public const string AuditTarget = "festival";
    public const int MinNameLength = 2;
    public const int MaxNameLength = 80;
    public const int MaxDays = 14;
    public const int MaxFestivals = 300;
    public const string InUse = "A trigger rule uses this event. Change or delete the rule first.";

    public static async Task<Results<Ok<FestivalView[]>, ValidationProblem>> ListAsync(
        DateOnly? from,
        DateOnly? to,
        TriggersDbContext db,
        FestivalCalendar calendar,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (from is { } start && to is { } end && end < start)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Use a date on or after from."] });
        }

        await calendar.EnsureSeededAsync(cancellationToken);
        var since = from ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var festivals = await db.BrandFestivals.AsNoTracking()
            .Where(festival => festival.EndsOn >= since && (to == null || festival.StartsOn <= to))
            .OrderBy(festival => festival.StartsOn)
            .ThenBy(festival => festival.Name)
            .ToListAsync(cancellationToken);
        var uses = await UsesAsync(db, cancellationToken);
        return TypedResults.Ok(festivals.Select(festival => FestivalView.Of(festival, uses.GetValueOrDefault(festival.Id))).ToArray());
    }

    public static async Task<Results<Created<FestivalView>, ValidationProblem, ProblemHttpResult>> AddAsync(
        FestivalRequest request,
        TriggersDbContext db,
        KairoDatabase database,
        FestivalCalendar calendar,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (checkedRequest, errors) = Check(request);
        if (checkedRequest is not { } valid)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await calendar.EnsureSeededAsync(cancellationToken);
        await database.LockAsync(FestivalCalendar.LockName(member.BrandId), cancellationToken);
        if (await db.BrandFestivals.CountAsync(cancellationToken) >= MaxFestivals)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"Your calendar can hold up to {MaxFestivals} events.");
        }

        var festival = new FestivalEvent
        {
            Id = Guid.NewGuid(),
            Origin = FestivalOrigins.Brand,
            CreatedBy = member.UserId,
            CreatedAt = time.GetUtcNow(),
        };
        valid.ApplyTo(festival);
        db.Festivals.Add(festival);
        await db.SaveChangesAsync(cancellationToken);
        var view = FestivalView.Of(festival, 0);
        await audit.RecordAsync(new AuditRecord("festival.added", AuditTarget, festival.Id.ToString(), After: view), cancellationToken);
        await live.PublishAsync(TriggerLiveEvents.FestivalsChanged(), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/triggers/festivals/{festival.Id}", view);
    }

    public static async Task<Results<Ok<FestivalView>, NotFound, ValidationProblem>> UpdateAsync(
        Guid festivalId,
        FestivalRequest request,
        TriggersDbContext db,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        var (checkedRequest, errors) = Check(request);
        if (checkedRequest is not { } valid)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.BrandFestivals.SingleOrDefaultAsync(festival => festival.Id == festivalId, cancellationToken) is not { } festival)
        {
            return TypedResults.NotFound();
        }

        var uses = await UsesAsync(db, cancellationToken);
        var before = FestivalView.Of(festival, uses.GetValueOrDefault(festival.Id));
        valid.ApplyTo(festival);
        await db.SaveChangesAsync(cancellationToken);
        var view = FestivalView.Of(festival, uses.GetValueOrDefault(festival.Id));
        await audit.RecordAsync(new AuditRecord("festival.updated", AuditTarget, festival.Id.ToString(), before, view), cancellationToken);
        await live.PublishAsync(TriggerLiveEvents.FestivalsChanged(), cancellationToken);
        return TypedResults.Ok(view);
    }

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid festivalId,
        TriggersDbContext db,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        if (await db.BrandFestivals.SingleOrDefaultAsync(festival => festival.Id == festivalId, cancellationToken) is not { } festival)
        {
            return TypedResults.NotFound();
        }

        if ((await UsesAsync(db, cancellationToken)).GetValueOrDefault(festival.Id) > 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: InUse);
        }

        db.Festivals.Remove(festival);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("festival.removed", AuditTarget, festival.Id.ToString(), Before: FestivalView.Of(festival, 0)), cancellationToken);
        await live.PublishAsync(TriggerLiveEvents.FestivalsChanged(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<Guid, int>> UsesAsync(TriggersDbContext db, CancellationToken cancellationToken)
    {
        var rules = await db.BrandRules.AsNoTracking()
            .Where(rule => rule.Source == TriggerSources.Festival)
            .Select(rule => rule.Condition.EventId)
            .ToListAsync(cancellationToken);
        return rules.OfType<Guid>().GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    }

    private static (CheckedFestival? Festival, Dictionary<string, string[]> Errors) Check(FestivalRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = string.Join(' ', (request.Name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < MinNameLength or > MaxNameLength)
        {
            errors["name"] = [$"Use {MinNameLength} to {MaxNameLength} characters."];
        }

        if (request.StartsOn is not { } startsOn)
        {
            errors["startsOn"] = ["Choose the date it starts."];
            return (null, errors);
        }

        var endsOn = request.EndsOn ?? startsOn;
        if (endsOn < startsOn)
        {
            errors["endsOn"] = ["Use a date on or after the start."];
        }
        else if (endsOn.DayNumber - startsOn.DayNumber + 1 > MaxDays)
        {
            errors["endsOn"] = [$"An event can last up to {MaxDays} days."];
        }

        var regions = (request.Regions ?? []).Select(id => id?.Trim() ?? "").Where(id => id != IndiaRegions.CountryId).Distinct(StringComparer.Ordinal).ToList();
        if (regions.Any(id => IndiaRegions.Find(id) is not { Kind: RegionKinds.State }))
        {
            errors["regions"] = ["Choose states from the list, or none for all of India."];
        }

        return errors.Count > 0 ? (null, errors) : (new CheckedFestival(name, startsOn, endsOn, regions), errors);
    }

    private sealed record CheckedFestival(string Name, DateOnly StartsOn, DateOnly EndsOn, List<string> Regions)
    {
        public void ApplyTo(FestivalEvent festival)
        {
            festival.Name = Name;
            festival.StartsOn = StartsOn;
            festival.EndsOn = EndsOn;
            festival.Regions = Regions;
        }
    }
}
