using System.Globalization;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed record CampaignQuery(
    string? Stage,
    string? Status,
    Guid? Owner,
    string? From,
    string? To,
    string? Q,
    string? Before,
    int? Limit);

internal static class CampaignEndpoints
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;
    public const int MaxSearchLength = 100;
    public const int ActivityLimit = 50;

    public static async Task<Results<Ok<CampaignPage>, ValidationProblem>> ListAsync(
        [AsParameters] CampaignQuery query,
        CampaignRoomsDbContext db,
        CampaignReader reader,
        IBrandDirectory brands,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Stage is { } stage && !RoomStates.All.Contains(stage))
        {
            errors["stage"] = [$"Use one of: {string.Join(", ", RoomStates.All)}."];
        }

        if (query.Status is { } status && !RoomStatuses.All.Contains(status))
        {
            errors["status"] = [$"Use one of: {string.Join(", ", RoomStatuses.All)}."];
        }

        var search = query.Q?.Trim();
        if (search is { Length: > MaxSearchLength })
        {
            errors["q"] = [$"Search with {MaxSearchLength} characters or fewer."];
        }

        var zone = await ZoneAsync(brands, member.BrandId, cancellationToken);
        var from = Day(query.From, "from", zone, errors);
        var to = Day(query.To, "to", zone, errors);
        if (from is not null && to is not null && to < from)
        {
            errors["to"] = ["Pick an end date on or after the start date."];
        }

        var cursor = Cursor.TryParse(query.Before, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rooms = db.Rooms.AsNoTracking();
        if (query.Stage is { } chosenStage)
        {
            rooms = rooms.Where(room => room.State == chosenStage);
        }

        if (query.Status is { } chosenStatus)
        {
            rooms = rooms.Where(room => room.Status == chosenStatus);
        }

        if (query.Owner is { } owner)
        {
            rooms = rooms.Where(room => room.OwnerId == owner);
        }

        if (from is { } start)
        {
            rooms = rooms.Where(room => room.CreatedAt >= start);
        }

        if (to is { } end)
        {
            var until = end.AddDays(1);
            rooms = rooms.Where(room => room.CreatedAt < until);
        }

        if (!string.IsNullOrEmpty(search))
        {
            rooms = rooms.Where(room => room.Title.Contains(search) || room.Products.Any(product => product.Title.Contains(search)));
        }

        if (cursor is { } after)
        {
            rooms = rooms.Where(room => room.CreatedAt < after);
        }

        var take = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var found = await rooms
            .OrderByDescending(room => room.CreatedAt)
            .ThenByDescending(room => room.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = found.Take(take).ToList();
        var people = await reader.MembersAsync(cancellationToken);
        return TypedResults.Ok(new CampaignPage(
            [.. page.Select(room => CampaignReader.Card(room, people))],
            found.Count > take ? Cursor.Of(page[^1]) : null));
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound>> GetAsync(
        Guid campaignId,
        CampaignRoomsDbContext db,
        CampaignReader reader,
        CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == campaignId, cancellationToken);
        return room is null ? TypedResults.NotFound() : TypedResults.Ok(await reader.DetailAsync(room, cancellationToken));
    }

    public static async Task<Results<Ok<CampaignActivityPage>, NotFound, ValidationProblem>> ActivityAsync(
        Guid campaignId,
        string? before,
        CampaignRoomsDbContext db,
        IAuditTrail trail,
        CancellationToken cancellationToken)
    {
        long? cursor = null;
        if (!string.IsNullOrWhiteSpace(before))
        {
            if (long.TryParse(before.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                cursor = id;
            }
            else
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Use the nextBefore value from the previous page."] });
            }
        }

        if (!await db.Rooms.AnyAsync(room => room.Id == campaignId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var page = await trail.ForTargetAsync(RoomWorkflow.AuditTarget, campaignId.ToString(), cursor, ActivityLimit, cancellationToken);
        var labels = AuditActions.All.ToDictionary(pair => pair.Key, pair => pair.Value);
        return TypedResults.Ok(new CampaignActivityPage(
            [.. page.Entries.Select(entry => new CampaignActivityView(
                entry.Id,
                entry.OccurredAt,
                entry.ActorType.ToString().ToLowerInvariant(),
                entry.ActorName,
                entry.Action,
                labels.GetValueOrDefault(entry.Action, entry.Action),
                entry.After))],
            page.NextBefore?.ToString(CultureInfo.InvariantCulture)));
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> RequestApprovalAsync(
        Guid campaignId,
        ApprovalRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var gate = Gate(request.Gate, errors);
        var version = Version(request.Version, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await ResultAsync(await workflow.RequestApprovalAsync(campaignId, gate!, version, member, cancellationToken), reader, cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> ApproveAsync(
        Guid campaignId,
        ApproveRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var gate = Gate(request.Gate, errors);
        var version = Version(request.Version, errors);
        if (request.Comment is { Length: > RoomLimits.MaxCommentLength })
        {
            errors["comment"] = [$"Keep the comment to {RoomLimits.MaxCommentLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await ResultAsync(await workflow.ApproveAsync(campaignId, gate!, version, request.Comment, member, cancellationToken), reader, cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> SendBackAsync(
        Guid campaignId,
        SendBackRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var version = Version(request.Version, errors);
        if (request.To is not { } to || !RoomStates.Stages.Contains(to))
        {
            errors["to"] = ["Choose the stage to send the work back to."];
        }

        var comment = request.Comment?.Trim() ?? "";
        if (comment.Length is < RoomLimits.MinCommentLength or > RoomLimits.MaxCommentLength)
        {
            errors["comment"] = [$"Say what needs to change, in {RoomLimits.MinCommentLength} to {RoomLimits.MaxCommentLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await ResultAsync(await workflow.SendBackAsync(campaignId, request.To!, comment, version, member, cancellationToken), reader, cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> DiscardAsync(
        Guid campaignId,
        DiscardRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var version = Version(request.Version, errors);
        if (request.Reason is { Length: > RoomLimits.MaxReasonLength })
        {
            errors["reason"] = [$"Keep the reason to {RoomLimits.MaxReasonLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await ResultAsync(await workflow.DiscardAsync(campaignId, request.Reason, version, cancellationToken), reader, cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> EndTestAsync(
        Guid campaignId,
        EndTestRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var version = Version(request.Version, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await ResultAsync(await workflow.EndTestAsync(campaignId, version, cancellationToken), reader, cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult>> RetryAsync(
        Guid campaignId,
        Guid runId,
        CampaignRoomsDbContext db,
        RoomWorkflow workflow,
        StageQueue queue,
        IAuditLog audit,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        if (await workflow.LockedAsync(campaignId, cancellationToken) is not { } room)
        {
            return TypedResults.NotFound();
        }

        var latest = await queue.LatestAsync(room.Id, room.State, cancellationToken);
        if (latest is null || latest.Id != runId)
        {
            return TypedResults.NotFound();
        }

        if (latest.Status != StageRunStatuses.Failed || queue.Find(latest.Kind) is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only the last failed step can be tried again.");
        }

        var run = await queue.EnqueueAsync(room, latest.Kind, member.UserId, cancellationToken, latest.Input);
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("stage_run.retried", RoomWorkflow.AuditTarget, room.Id.ToString(), new { runId = latest.Id }, new { runId = run.Id, run.Kind }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(room, cancellationToken));
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> ResultAsync(
        MoveResult result,
        CampaignReader reader,
        CancellationToken cancellationToken) => result.Error switch
        {
            MoveError.None => TypedResults.Ok(await reader.DetailAsync(result.Room!, cancellationToken)),
            MoveError.NotFound => TypedResults.NotFound(),
            MoveError.NotApprover => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: result.Message),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message),
        };

    private static string? Gate(string? gate, Dictionary<string, string[]> errors)
    {
        if (gate is null || !RoomGates.All.Contains(gate))
        {
            errors["gate"] = [$"Use one of: {string.Join(", ", RoomGates.All)}."];
            return null;
        }

        return gate;
    }

    private static int Version(int? version, Dictionary<string, string[]> errors)
    {
        if (version is not { } value || value < 0)
        {
            errors["version"] = ["Send the campaign's version from when you opened it."];
            return 0;
        }

        return value;
    }

    private static DateTimeOffset? Day(string? text, string field, TimeZoneInfo zone, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            errors[field] = ["Use a date like 2026-10-15."];
            return null;
        }

        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static async Task<TimeZoneInfo> ZoneAsync(IBrandDirectory brands, Guid brandId, CancellationToken cancellationToken)
    {
        var id = await brands.FindTimeZoneAsync(brandId, cancellationToken);
        return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    private static class Cursor
    {
        public static string Of(Room room) => room.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

        public static DateTimeOffset? TryParse(string? text, Dictionary<string, string[]> errors)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and <= 3155378975999999999)
            {
                return new DateTimeOffset(ticks, TimeSpan.Zero);
            }

            errors["before"] = ["Use the nextBefore value from the previous page."];
            return null;
        }
    }
}
