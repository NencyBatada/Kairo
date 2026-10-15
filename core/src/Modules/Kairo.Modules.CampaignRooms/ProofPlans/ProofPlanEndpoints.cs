using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed record ProofRegionView(
    string Id,
    string Name,
    string? State,
    double? Latitude,
    double? Longitude,
    bool InArea,
    double? MatchScore,
    double? Share);

internal sealed record ProofWindowView(DateOnly? Start, DateOnly? End, int? Days);

internal sealed record ProofLiftView(int Days, double? Lift);

internal sealed record ProofCostView(decimal Amount, string Currency);

internal sealed record ProofWarningView(string Code, string Message);

internal sealed record ProofPlanView(
    int Version,
    int BriefVersion,
    string Unit,
    string Kpi,
    bool InsideArea,
    bool ChosenByUser,
    ProofRegionView[] TestRegions,
    ProofRegionView[] Holdouts,
    ProofRegionView[] Candidates,
    ProofWindowView Window,
    double? MinDetectableLift,
    ProofLiftView[] LiftByWindow,
    double? HoldoutShare,
    ProofCostView? HoldoutCost,
    int DataDays,
    int PlaceboWindows,
    ProofWarningView[] Warnings,
    DateTimeOffset? LockedAt,
    bool ChangedAfterLock,
    DateTimeOffset CreatedAt);

internal sealed record ProofPlanResult(ProofPlanView? Plan, bool Updating, bool CanChooseHoldouts);

internal sealed record HoldoutRequest(string[]? RegionIds, int? Version);

internal static class ProofPlanReader
{
    public static Task<ProofPlan?> LatestAsync(CampaignRoomsDbContext db, Guid roomId, CancellationToken cancellationToken) =>
        db.ProofPlans.AsNoTracking()
            .Where(plan => plan.RoomId == roomId)
            .OrderByDescending(plan => plan.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public static ProofPlanView View(ProofPlan plan)
    {
        var content = plan.Read();
        return new ProofPlanView(
            plan.Version,
            plan.BriefVersion,
            content.Unit,
            content.Kpi,
            content.InsideArea,
            content.ChosenByUser,
            [.. content.TestRegions.Select(Region)],
            [.. content.Holdouts.Select(Region)],
            [.. content.Candidates.Select(Region)],
            new ProofWindowView(content.StartDate, content.EndDate, content.WindowDays),
            content.MinDetectableLift,
            [.. content.Lifts.Select(lift => new ProofLiftView(lift.Days, lift.Lift))],
            content.HoldoutShare,
            content.HoldoutCost is { } cost && content.Currency is { Length: > 0 } currency ? new ProofCostView(cost, currency) : null,
            content.DataDays,
            content.PlaceboWindows,
            [.. content.Warnings.Select(warning => new ProofWarningView(warning.Code, warning.Message))],
            plan.LockedAt,
            plan.ChangedAfterLock,
            plan.CreatedAt);
    }

    private static ProofRegionView Region(PlanRegion region) =>
        new(region.Id, region.Name, region.State, region.Latitude, region.Longitude, region.InArea, region.Score, region.Share);
}

internal static class ProofPlanEndpoints
{
    public const int MaxHoldouts = 10;

    public static async Task<Results<Ok<ProofPlanResult>, NotFound>> GetAsync(
        Guid campaignId,
        CampaignRoomsDbContext db,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == campaignId, cancellationToken);
        if (room is null)
        {
            return TypedResults.NotFound();
        }

        var plan = await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken);
        var updating = await db.StageRuns.AnyAsync(
            run => run.RoomId == room.Id && run.Kind == StageKinds.ProofPlan
                && (run.Status == StageRunStatuses.Queued || run.Status == StageRunStatuses.Running),
            cancellationToken);
        return TypedResults.Ok(new ProofPlanResult(
            plan is null ? null : ProofPlanReader.View(plan),
            updating,
            member.Has(BrandRoles.Marketer) && Blocker(room, plan) is null));
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> ChooseHoldoutsAsync(
        Guid campaignId,
        HoldoutRequest request,
        CampaignRoomsDbContext db,
        RoomWorkflow workflow,
        StageQueue queue,
        IAuditLog audit,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not { } version || version < 0)
        {
            errors["version"] = ["Send the campaign's version from when you opened it."];
            version = 0;
        }

        var chosen = (request.RegionIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (chosen.Count is 0 or > MaxHoldouts)
        {
            errors["regionIds"] = [$"Choose 1 to {MaxHoldouts} holdout regions."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await workflow.LockedAsync(campaignId, cancellationToken) is not { } room)
        {
            return TypedResults.NotFound();
        }

        if (room.Version != version)
        {
            return Conflict("This campaign changed since you opened it. Refresh and try again.");
        }

        var plan = await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken);
        if (Blocker(room, plan) is { } blocker)
        {
            return Conflict(blocker);
        }

        var content = plan!.Read();
        var candidates = content.Candidates.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        if (chosen.FirstOrDefault(id => !candidates.Contains(id)) is { } unknown)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["regionIds"] = [$"{unknown} isn't one of the suggested holdout regions."] });
        }

        var area = content.TestRegions.Concat(content.Holdouts).Where(region => region.InArea).Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        if (area.Count > 0 && area.Except(chosen).Count() == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["regionIds"] = ["Leave at least one region in the test."] });
        }

        var run = await queue.EnqueueAsync(
            room,
            StageKinds.ProofPlan,
            member.UserId,
            cancellationToken,
            JsonSerializer.Serialize(new HoldoutChoice([.. chosen]), JsonSerializerOptions.Web));
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "proof_plan.holdouts_chosen",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                new { holdouts = content.Holdouts.Select(region => region.Id).ToArray() },
                new { holdouts = chosen, runId = run.Id }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return TypedResults.Ok(await reader.DetailAsync(room, cancellationToken));
    }

    private static string? Blocker(Room room, ProofPlan? plan) => room switch
    {
        _ when plan is null => "There's no test plan yet.",
        _ when plan.LockedAt is not null => "The test plan is locked.",
        { State: not RoomStates.BriefDraft } => "Holdout regions can only change while the brief is a draft.",
        { PendingGate: not null } => "Approval has been requested, so the test plan can't change.",
        { RunStatus: StageRunStatuses.Queued or StageRunStatuses.Running } => "Wait for the agent to finish first.",
        _ => null,
    };

    private static ProblemHttpResult Conflict(string message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: message);
}
