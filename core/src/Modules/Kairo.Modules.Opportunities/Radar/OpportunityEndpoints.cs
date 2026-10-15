using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Live;
using Kairo.Modules.Opportunities.Data;
using Kairo.Modules.Opportunities.Refresh;

namespace Kairo.Modules.Opportunities.Radar;

internal static class OpportunityEndpoints
{
    public const string AuditTarget = "opportunity";
    public const string Open = "open";
    public const int MaxListed = 200;
    public const int MaxNoteLength = 500;
    public const string NotInStock = "None of the products that match this opportunity is in stock.";

    private static readonly string[] Statuses =
    [
        Open,
        OpportunityStatuses.New,
        OpportunityStatuses.Saved,
        OpportunityStatuses.Accepted,
        OpportunityStatuses.Dismissed,
        OpportunityStatuses.Expired,
    ];

    public static async Task<Results<Ok<OpportunityList>, ValidationProblem>> ListAsync(
        OpportunitiesDbContext db,
        OpportunityReader reader,
        string? status,
        string? category,
        bool? watched,
        int? minScore,
        bool? triggered,
        CancellationToken cancellationToken)
    {
        var wanted = status ?? Open;
        var errors = new Dictionary<string, string[]>();
        if (!Statuses.Contains(wanted))
        {
            errors["status"] = ["Use open, new, saved, accepted, dismissed or expired."];
        }

        if (minScore is < 0 or > 100)
        {
            errors["minScore"] = ["Use a score from 0 to 100."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        string[] statuses = wanted == Open ? [OpportunityStatuses.New, OpportunityStatuses.Saved] : [wanted];
        var opportunities = await db.BrandOpportunities.AsNoTracking()
            .Where(o => statuses.Contains(o.Status))
            .OrderByDescending(o => o.Score)
            .ThenByDescending(o => o.LastSeenAt)
            .Take(MaxListed)
            .ToListAsync(cancellationToken);
        var shelf = await reader.ShelfAsync(opportunities, cancellationToken);
        var openOnly = statuses.All(s => s is OpportunityStatuses.New or OpportunityStatuses.Saved);
        var cards = opportunities
            .Where(o => !shelf.Ignored(o))
            .Where(o => string.IsNullOrWhiteSpace(category) || o.MatchedCategories.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(o => OpportunityCard.Of(o, shelf.InStock(o), shelf.Watched(o)))
            .Where(card => !openOnly || (card.Available && (card.Score >= OpportunityOptions.CutOff || card.Watched || card.Trigger is not null)))
            .Where(card => watched is null || card.Watched == watched)
            .Where(card => triggered is null || (card.Trigger is not null) == triggered)
            .Where(card => minScore is null || card.Score >= minScore)
            .OrderByDescending(card => card.Trigger is not null)
            .ToArray();

        var latest = await db.BrandRuns.AsNoTracking()
            .Where(run => run.Outcome != BrandRefresher.Running)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var refreshedAt = await db.BrandRuns.AsNoTracking()
            .Where(run => run.Outcome == RefreshOutcomes.Succeeded)
            .MaxAsync(run => (DateTimeOffset?)run.FinishedAt, cancellationToken);
        return TypedResults.Ok(new OpportunityList(refreshedAt, latest?.Outcome, latest?.Reason, cards));
    }

    public static async Task<Results<Ok<OpportunityDetail>, NotFound>> GetAsync(
        Guid opportunityId,
        OpportunitiesDbContext db,
        OpportunityReader reader,
        CancellationToken cancellationToken)
    {
        if (await db.BrandOpportunities.AsNoTracking().SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken) is not { } opportunity)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await reader.DetailAsync(opportunity, cancellationToken));
    }

    public static async Task<Results<Ok<AcceptedView>, NotFound, ProblemHttpResult>> AcceptAsync(
        Guid opportunityId,
        OpportunitiesDbContext db,
        KairoDatabase database,
        OpportunityReader reader,
        ICampaignRooms rooms,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, db, database, cancellationToken) is not { } opportunity)
        {
            return TypedResults.NotFound();
        }

        if (!opportunity.IsOpen)
        {
            return Conflict(opportunity);
        }

        var evidence = await reader.DetailAsync(opportunity, cancellationToken);
        if (!evidence.Available)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: NotInStock);
        }

        var before = opportunity.Status;
        var roomId = await rooms.OpenAsync(
            new RoomOpening(opportunity.Id, opportunity.Topic, Evidence(evidence, opportunity), member.UserId),
            cancellationToken);
        Close(opportunity, OpportunityStatuses.Accepted, member, time);
        opportunity.RoomId = roomId;
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(new AuditRecord("opportunity.accepted", AuditTarget, opportunity.Id.ToString(), Before(opportunity, before), new { opportunity.Status, roomId }), opportunity, audit, live, cancellationToken);
        return TypedResults.Ok(new AcceptedView(roomId, await reader.DetailAsync(opportunity, cancellationToken)));
    }

    public static JsonElement Evidence(OpportunityDetail detail, Opportunity opportunity)
    {
        var evidence = JsonSerializer.SerializeToNode(detail, JsonSerializerOptions.Web)!.AsObject();
        if (opportunity.Bids is { } bids)
        {
            evidence["bids"] = new JsonObject { ["low"] = bids.Low, ["high"] = bids.High };
        }

        return JsonSerializer.SerializeToElement(evidence, JsonSerializerOptions.Web);
    }

    public static async Task<Results<Ok<OpportunityDetail>, NotFound, ProblemHttpResult>> SaveAsync(
        Guid opportunityId,
        OpportunitiesDbContext db,
        KairoDatabase database,
        OpportunityReader reader,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, db, database, cancellationToken) is not { } opportunity)
        {
            return TypedResults.NotFound();
        }

        if (opportunity.Status != OpportunityStatuses.New)
        {
            return Conflict(opportunity);
        }

        opportunity.Status = OpportunityStatuses.Saved;
        opportunity.SavedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(new AuditRecord("opportunity.saved", AuditTarget, opportunity.Id.ToString(), Before(opportunity, OpportunityStatuses.New), new { opportunity.Status }), opportunity, audit, live, cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(opportunity, cancellationToken));
    }

    public static async Task<Results<Ok<OpportunityDetail>, NotFound, ProblemHttpResult>> UnsaveAsync(
        Guid opportunityId,
        OpportunitiesDbContext db,
        KairoDatabase database,
        OpportunityReader reader,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        if (await LockedAsync(opportunityId, db, database, cancellationToken) is not { } opportunity)
        {
            return TypedResults.NotFound();
        }

        if (opportunity.Status != OpportunityStatuses.Saved)
        {
            return Conflict(opportunity);
        }

        opportunity.Status = OpportunityStatuses.New;
        opportunity.SavedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(new AuditRecord("opportunity.unsaved", AuditTarget, opportunity.Id.ToString(), Before(opportunity, OpportunityStatuses.Saved), new { opportunity.Status }), opportunity, audit, live, cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(opportunity, cancellationToken));
    }

    public static async Task<Results<Ok<OpportunityDetail>, NotFound, ValidationProblem, ProblemHttpResult>> DismissAsync(
        Guid opportunityId,
        DismissRequest request,
        OpportunitiesDbContext db,
        KairoDatabase database,
        OpportunityReader reader,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.Reason is null || !DismissReasons.All.Contains(request.Reason))
        {
            errors["reason"] = ["Use not_relevant, too_small, wrong_timing, already_covered or other."];
        }
        else if (request.Reason == DismissReasons.Other && note is null)
        {
            errors["note"] = ["Say why you're dismissing it."];
        }

        if (note is { Length: > MaxNoteLength })
        {
            errors["note"] = [$"Use up to {MaxNoteLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await LockedAsync(opportunityId, db, database, cancellationToken) is not { } opportunity)
        {
            return TypedResults.NotFound();
        }

        if (!opportunity.IsOpen)
        {
            return Conflict(opportunity);
        }

        var before = opportunity.Status;
        Close(opportunity, OpportunityStatuses.Dismissed, member, time);
        opportunity.DismissReason = request.Reason;
        opportunity.DismissNote = note;
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(
            new AuditRecord(
                "opportunity.dismissed",
                AuditTarget,
                opportunity.Id.ToString(),
                Before(opportunity, before),
                new { opportunity.Status, opportunity.DismissReason, opportunity.DismissNote }),
            opportunity,
            audit,
            live,
            cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(opportunity, cancellationToken));
    }

    private static async Task<Opportunity?> LockedAsync(Guid opportunityId, OpportunitiesDbContext db, KairoDatabase database, CancellationToken cancellationToken)
    {
        await database.LockAsync($"opportunity:{opportunityId:N}", cancellationToken);
        return await db.BrandOpportunities.SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);
    }

    private static void Close(Opportunity opportunity, string status, CurrentMember member, TimeProvider time)
    {
        opportunity.Status = status;
        opportunity.ClosedAt = time.GetUtcNow();
        opportunity.ClosedBy = member.UserId;
        opportunity.GrowthAtClose = opportunity.Growth;
    }

    private static ProblemHttpResult Conflict(Opportunity opportunity) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"This opportunity is already {opportunity.Status}.");

    private static object Before(Opportunity opportunity, string status) => new { Status = status, opportunity.Topic };

    private static async Task RecordAsync(AuditRecord record, Opportunity opportunity, IAuditLog audit, ILiveUpdates live, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(record, cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.Changed(opportunity), cancellationToken);
    }
}
