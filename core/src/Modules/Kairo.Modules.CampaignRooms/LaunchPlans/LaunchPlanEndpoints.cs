using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Launch;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.LaunchPlans;

internal sealed record LaunchPlanUpdate(int Version, LaunchSettings Settings);
internal sealed record LaunchDailyBudget(string CampaignType, decimal Total, long DailyMicros);
internal sealed record LaunchPlanView(int Version, LaunchPlan? Plan, string? Hash, string? Blocker, LaunchDailyBudget[] DailyBudgets);

internal static class LaunchPlanEndpoints
{
    public static async Task<Results<Ok<LaunchPlanView>, NotFound>> GetAsync(Guid campaignId, CampaignRoomsDbContext db, LaunchPlanDesk desk, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == campaignId, cancellationToken);
        return room is null ? TypedResults.NotFound() : TypedResults.Ok(await ViewAsync(room, desk, cancellationToken));
    }

    public static async Task<Results<Ok<LaunchPlanView>, NotFound, ProblemHttpResult>> UpdateAsync(Guid campaignId, LaunchPlanUpdate request,
        RoomWorkflow workflow, CampaignRoomsDbContext db, LaunchPlanDesk desk, IApprovalLocks approvals, IAuditLog audit, CancellationToken cancellationToken)
    {
        var room = await workflow.LockedAsync(campaignId, cancellationToken);
        if (room is null) return TypedResults.NotFound();
        if (room.Version != request.Version) return Conflict("This campaign changed. Refresh before saving the launch settings.");
        if (room.State != RoomStates.ReadyToLaunch || room.PendingGate is not null) return Conflict("Launch settings can be changed only before requesting launch approval.");
        if (request.Settings?.Channels is null || request.Settings.Channels.Any(channel => channel is null)) return Conflict("Send channel budgets and bidding settings.");
        var draft = await db.LaunchPlans.SingleOrDefaultAsync(row => row.RoomId == room.Id, cancellationToken);
        var before = draft?.Settings;
        if (draft is null)
        {
            draft = new LaunchPlanDraft { RoomId = room.Id };
            db.LaunchPlans.Add(draft);
        }

        draft.Settings = JsonSerializer.Serialize(request.Settings with
        {
            Channels = [.. request.Settings.Channels.OrderBy(channel => channel.CampaignType, StringComparer.Ordinal)],
            AudienceIds = [.. (request.Settings.AudienceIds ?? []).Distinct().Order(StringComparer.Ordinal)],
            SalesCountry = request.Settings.SalesCountry?.ToUpperInvariant(),
        }, JsonSerializerOptions.Web);
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        if (await desk.GetAsync(room.Id, cancellationToken) is not { } plan) return Conflict("The brief and test plan aren't ready.");
        if (LaunchPlanDesk.Validate(plan) is { } error) return Conflict(error);
        var types = await db.Briefs.Where(row => row.RoomId == room.Id && row.Version == plan.BriefVersion).Select(row => row.Content.CampaignTypes).SingleAsync(cancellationToken);
        if (!types.ToHashSet(StringComparer.Ordinal).SetEquals(plan.Settings.Channels.Select(channel => channel.CampaignType))) return Conflict("Launch campaign types must match the approved brief.");
        await approvals.InvalidateAsync(room.Id, [RoomGates.Launch], cancellationToken);
        await audit.RecordAsync(new AuditRecord("launch_plan.updated", RoomWorkflow.AuditTarget, room.Id.ToString(), before, new { draft.Settings }), cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
        return TypedResults.Ok(await ViewAsync(room, desk, cancellationToken));
    }

    private static async Task<LaunchPlanView> ViewAsync(Room room, LaunchPlanDesk desk, CancellationToken cancellationToken)
    {
        var plan = await desk.GetAsync(room.Id, cancellationToken);
        return new LaunchPlanView(room.Version, plan, plan is null ? null : ApprovalHash.Of(LaunchPlanDesk.Serialize(plan)),
            await desk.BlockerAsync(room.Id, cancellationToken), plan is null ? [] :
            [.. plan.Settings.Channels.Select(channel => new LaunchDailyBudget(channel.CampaignType, channel.Budget,
                checked((long)decimal.Floor(channel.Budget * 1_000_000m / Math.Max(1, plan.End.DayNumber - plan.Start.DayNumber + 1)))))]);
    }

    private static ProblemHttpResult Conflict(string message) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: message);
}
