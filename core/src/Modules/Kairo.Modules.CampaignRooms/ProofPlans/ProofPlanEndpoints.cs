using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Briefs;
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
    bool ApprovedWithWarnings,
    string? ChangeReason,
    DateTimeOffset CreatedAt);

internal sealed record ProofPlanResult(ProofPlanView? Plan, bool Updating, bool CanChooseHoldouts, bool CanChangeAfterLock);

internal sealed record HoldoutRequest(string[]? RegionIds, int? Version);

internal sealed record ProofPlanChangeRequest(int? Version, string? Reason, string[]? HoldoutRegionIds, string? EndDate);

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
            plan.ApprovedWithWarnings,
            plan.ChangeReason,
            plan.CreatedAt);
    }

    private static ProofRegionView Region(PlanRegion region) =>
        new(region.Id, region.Name, region.State, region.Latitude, region.Longitude, region.InArea, region.Score, region.Share);
}

internal static class ProofPlanEndpoints
{
    public const int MaxHoldouts = 10;
    public const string ChangedAfterLock = "changed_after_lock";

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
            member.Has(BrandRoles.Marketer) && Blocker(room, plan) is null,
            member.Has(BrandRoles.Marketer) && LockedBlocker(room, plan) is null));
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

    public static async Task<Results<Ok<ProofPlanResult>, NotFound, ProblemHttpResult, ValidationProblem>> ChangeAsync(
        Guid campaignId,
        ProofPlanChangeRequest request,
        CampaignRoomsDbContext db,
        RoomWorkflow workflow,
        IAuditLog audit,
        CurrentMember member,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not { } version || version < 0)
        {
            errors["version"] = ["Send the campaign's version from when you opened it."];
            version = 0;
        }

        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < RoomLimits.MinCommentLength or > RoomLimits.MaxReasonLength)
        {
            errors["reason"] = [$"Say why the test plan has to change, in {RoomLimits.MinCommentLength} to {RoomLimits.MaxReasonLength} characters."];
        }

        var chosen = request.HoldoutRegionIds is null
            ? null
            : request.HoldoutRegionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (chosen is { Count: 0 or > MaxHoldouts })
        {
            errors["holdoutRegionIds"] = [$"Choose 1 to {MaxHoldouts} holdout regions."];
        }

        DateOnly? end = null;
        if (!string.IsNullOrWhiteSpace(request.EndDate))
        {
            end = BriefDesk.ParseDay(request.EndDate);
            if (end is null)
            {
                errors["endDate"] = ["Use a date like 2026-10-15."];
            }
        }
        else if (chosen is null)
        {
            errors["holdoutRegionIds"] = ["Change the holdout regions or the end date."];
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
        if (LockedBlocker(room, plan) is { } blocker)
        {
            return Conflict(blocker);
        }

        var content = plan!.Read();
        var holdoutsBefore = content.Holdouts.Select(region => region.Id).ToArray();
        var endBefore = content.EndDate;
        if (chosen is not null)
        {
            var known = content.TestRegions.Concat(content.Holdouts).Concat(content.Candidates)
                .GroupBy(region => region.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (chosen.FirstOrDefault(id => !known.ContainsKey(id)) is { } unknown)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["holdoutRegionIds"] = [$"{unknown} isn't one of the suggested holdout regions."] });
            }

            var tested = content.TestRegions.Concat(content.Holdouts.Where(region => region.InArea))
                .Where(region => !chosen.Contains(region.Id))
                .DistinctBy(region => region.Id)
                .ToList();
            if (tested.Count == 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["holdoutRegionIds"] = ["Leave at least one region in the test."] });
            }

            content.TestRegions = tested;
            content.Holdouts = [.. chosen.Select(id => known[id])];
        }

        if (end is { } last)
        {
            if (content.StartDate is { } start && last < start)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["endDate"] = [$"Pick an end date on or after the test's start, {BriefChecks.Day(start)}."] });
            }

            content.EndDate = last;
            content.WindowDays = content.StartDate is { } first ? last.DayNumber - first.DayNumber + 1 : content.WindowDays;
        }

        if (content.EndDate == endBefore && content.Holdouts.Select(region => region.Id).ToHashSet(StringComparer.Ordinal).SetEquals(holdoutsBefore))
        {
            return Conflict("Nothing in the test plan changed.");
        }

        content.Warnings.RemoveAll(warning => warning.Code == ChangedAfterLock);
        content.Warnings.Add(new PlanWarning(ChangedAfterLock, "The test plan changed after launch, so its smallest detectable lift and holdout cost are from before the change."));
        var latest = await db.ProofPlans.Where(row => row.RoomId == room.Id).MaxAsync(row => row.Version, cancellationToken);
        var changed = new ProofPlan
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Version = latest + 1,
            BriefVersion = plan.BriefVersion,
            Method = plan.Method,
            CreatedAt = time.GetUtcNow(),
            LockedAt = plan.LockedAt,
            ChangedAfterLock = true,
            ApprovedWithWarnings = plan.ApprovedWithWarnings,
            ChangeReason = reason,
            ChangedBy = member.UserId,
        };
        changed.Write(content);
        db.ProofPlans.Add(changed);
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "proof_plan.changed_after_lock",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                new { version = plan.Version, holdouts = holdoutsBefore, endDate = endBefore },
                new { version = changed.Version, holdouts = content.Holdouts.Select(region => region.Id).ToArray(), endDate = content.EndDate, reason }),
            cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return TypedResults.Ok(new ProofPlanResult(ProofPlanReader.View(changed), false, false, true));
    }

    private static string? LockedBlocker(Room room, ProofPlan? plan) => room switch
    {
        _ when plan is null => "There's no test plan yet.",
        _ when plan.LockedAt is null => "The test plan locks at launch. Until then, change the brief or choose holdout regions instead.",
        { State: RoomStates.Closed or RoomStates.Discarded } => "This campaign is closed, so its test plan can't change.",
        _ => null,
    };

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
