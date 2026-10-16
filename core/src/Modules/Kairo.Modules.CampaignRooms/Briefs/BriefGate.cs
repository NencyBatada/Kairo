using Kairo.Contracts.Identity;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal static class ApprovalRoutes
{
    public const string Approvers = "approvers";
    public const string BudgetOwner = "budget_owner";
}

internal sealed record BriefRoute(string Route, string? Reason)
{
    public static BriefRoute Approvers { get; } = new(ApprovalRoutes.Approvers, null);

    public bool ToBudgetOwner => Route == ApprovalRoutes.BudgetOwner;

    public static BriefRoute Of(BriefBudget? budget, BudgetCap cap)
    {
        if (budget is not { Amount: > 0 } amount || cap.Amount is not { } limit)
        {
            return Approvers;
        }

        if (!string.IsNullOrEmpty(cap.Currency) && !string.Equals(amount.Currency, cap.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new BriefRoute(
                ApprovalRoutes.BudgetOwner,
                $"The budget is in {amount.Currency} but the budget limit is in {cap.Currency}, so only a budget owner can approve it.");
        }

        return amount.Amount > limit
            ? new BriefRoute(
                ApprovalRoutes.BudgetOwner,
                $"The {BudgetGuide.Money(amount.Amount, amount.Currency)} budget is above the {BudgetGuide.Money(limit, cap.Currency)} limit, so only a budget owner can approve it.")
            : Approvers;
    }
}

internal sealed record BriefGateCheck(string? Problem, int BriefVersion, int PlanVersion, BriefRoute Route)
{
    public static BriefGateCheck Refused(string problem) => new(problem, 0, 0, BriefRoute.Approvers);
}

internal sealed class BriefGate(
    CampaignRoomsDbContext db,
    BriefDesk desk,
    IBudgetLimit limits,
    IApprovers approvers)
{
    public const string NoBudgetOwner = "This brief's budget needs a budget owner's approval, but nobody is chosen as budget owner. Ask an admin to choose one in Settings, Approvals.";

    public static readonly string[] PlanKinds = [StageKinds.Brief, StageKinds.StarterBrief, StageKinds.ProofPlan];

    public async Task<BriefGateCheck> CheckAsync(Room room, CancellationToken cancellationToken)
    {
        var busy = await db.StageRuns.AnyAsync(
            run => run.RoomId == room.Id
                && PlanKinds.Contains(run.Kind)
                && (run.Status == StageRunStatuses.Queued || run.Status == StageRunStatuses.Running),
            cancellationToken);
        if (busy)
        {
            return BriefGateCheck.Refused("Wait for the Plan agent to finish the brief and test plan before asking for approval.");
        }

        var brief = await db.Briefs.AsNoTracking()
            .Where(row => row.RoomId == room.Id)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (brief is null)
        {
            return BriefGateCheck.Refused("The brief isn't ready yet.");
        }

        var content = brief.Content.Normalized();
        var context = await desk.ReadAsync(room, content, false, cancellationToken);
        var blocking = BriefChecks.Run(content, context.Situation())
            .Where(warning => warning.Severity == BriefSeverities.Blocking)
            .Select(warning => warning.Message)
            .ToList();
        if (blocking.Count > 0)
        {
            return BriefGateCheck.Refused($"Fix the brief before asking for approval. {string.Join(" ", blocking)}");
        }

        var plan = await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken);
        if (plan is null)
        {
            return BriefGateCheck.Refused("The brief needs a test plan before approval. Try designing the test again.");
        }

        if (!await DesignedForAsync(plan, brief, cancellationToken))
        {
            return BriefGateCheck.Refused("The test plan was designed for an earlier version of the brief. Try designing the test again.");
        }

        var route = BriefRoute.Of(content.Budget, context.Limit);
        if (route.ToBudgetOwner && (await approvers.ForAsync(room.BrandId, ApprovalDuty.BudgetOwner, cancellationToken)).Count == 0)
        {
            return BriefGateCheck.Refused(NoBudgetOwner);
        }

        return new BriefGateCheck(null, brief.Version, plan.Version, route);
    }

    public async Task<BriefRoute> RouteAsync(Room room, CancellationToken cancellationToken)
    {
        var briefs = db.Briefs.AsNoTracking().Where(row => row.RoomId == room.Id);
        if (room.GateBriefVersion is { } version)
        {
            briefs = briefs.Where(row => row.Version == version);
        }

        var brief = await briefs.OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        return brief is null
            ? BriefRoute.Approvers
            : BriefRoute.Of(brief.Content.Normalized().Budget, await limits.GetAsync(room.BrandId, cancellationToken));
    }

    private async Task<bool> DesignedForAsync(ProofPlan plan, Brief latest, CancellationToken cancellationToken)
    {
        if (plan.BriefVersion == latest.Version)
        {
            return true;
        }

        var designed = await db.Briefs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.RoomId == latest.RoomId && row.Version == plan.BriefVersion, cancellationToken);
        return designed is not null && SameScope(designed.Content.Normalized(), latest.Content.Normalized());
    }

    public static bool SameScope(BriefContent designed, BriefContent latest) =>
        designed.StartDate == latest.StartDate
        && designed.EndDate == latest.EndDate
        && Names(designed.Regions).SetEquals(Names(latest.Regions));

    private static HashSet<string> Names(IEnumerable<string>? regions) =>
        (regions ?? []).Where(region => !string.IsNullOrWhiteSpace(region)).Select(region => region.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
