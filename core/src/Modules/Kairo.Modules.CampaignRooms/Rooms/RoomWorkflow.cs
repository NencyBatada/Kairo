using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal enum MoveError
{
    None,
    NotFound,
    Stale,
    NotAllowed,
    NotApprover,
}

internal sealed record MoveResult(Room? Room, MoveError Error, string? Message = null)
{
    public static MoveResult Done(Room room) => new(room, MoveError.None);

    public static MoveResult Failed(MoveError error, string? message = null) => new(null, error, message);
}

internal static class RoomLimits
{
    public const int MinCommentLength = 2;
    public const int MaxCommentLength = 1000;
    public const int MaxReasonLength = 500;
}

internal sealed class RoomWorkflow(
    CampaignRoomsDbContext db,
    KairoDatabase database,
    IApprovers approvers,
    IBrandDirectory brands,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    TimeProvider time)
{
    public const string AuditTarget = "campaign_room";
    public const string ApprovalKind = "approval.needed";
    public const string ReturnedKind = "work.returned";

    public static string LockName(Guid roomId) => $"room:{roomId:N}";

    public static string GateSubject(Guid roomId, string gate) => $"room:{roomId:N}:{gate}";

    public static ApprovalDuty DutyOf(string gate) => gate switch
    {
        RoomGates.Brief => ApprovalDuty.Brief,
        RoomGates.Assets => ApprovalDuty.Assets,
        _ => ApprovalDuty.Launch,
    };

    public async Task<Room?> LockedAsync(Guid roomId, CancellationToken cancellationToken)
    {
        await database.LockAsync(LockName(roomId), cancellationToken);
        return await db.Rooms.SingleOrDefaultAsync(room => room.Id == roomId, cancellationToken);
    }

    public async Task<IReadOnlyList<BrandMember>> ApproversAsync(Guid brandId, string gate, IReadOnlyList<BrandMember> members, CancellationToken cancellationToken)
    {
        var assigned = await approvers.ForAsync(brandId, DutyOf(gate), cancellationToken);
        return assigned.Count > 0 ? assigned : members.Where(member => (member.Roles & BrandRoles.Approver) != BrandRoles.None).ToList();
    }

    public async Task<bool> MayApproveAsync(Guid brandId, string gate, CurrentMember member, CancellationToken cancellationToken)
    {
        if (!member.Has(BrandRoles.Approver))
        {
            return false;
        }

        var assigned = await approvers.ForAsync(brandId, DutyOf(gate), cancellationToken);
        return assigned.Count == 0 || assigned.Any(approver => approver.UserId == member.UserId);
    }

    public async Task<MoveResult> RequestApprovalAsync(Guid roomId, string gate, int version, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, version, cancellationToken) is not { Room: { } room } opened)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (opened.Error != MoveError.None)
        {
            return opened;
        }

        if (!RoomLifecycle.MarketerRequests(gate) || RoomLifecycle.GateFor(room.State) != gate)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "This campaign isn't at that approval step.");
        }

        if (room.PendingGate is not null)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Approval has already been requested.");
        }

        if (room.Status == RoomStatuses.AgentWorking)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Wait for the agent to finish before asking for approval.");
        }

        if (gate == RoomGates.Brief && !await db.Briefs.AnyAsync(brief => brief.RoomId == room.Id, cancellationToken))
        {
            return MoveResult.Failed(MoveError.NotAllowed, "The brief isn't ready yet.");
        }

        var before = Snapshot(room);
        await RequestGateAsync(room, gate, member.UserId, cancellationToken);
        Touch(room);
        await FinishAsync(room, new AuditRecord("campaign_room.approval_requested", AuditTarget, room.Id.ToString(), before, After(room, new { gate })), cancellationToken);
        return MoveResult.Done(room);
    }

    public async Task<MoveResult> ApproveAsync(Guid roomId, string gate, int version, string? comment, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, version, cancellationToken) is not { Room: { } room } opened)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (opened.Error != MoveError.None)
        {
            return opened;
        }

        if (room.PendingGate != gate || RoomLifecycle.AfterApproval(gate) is not { } next)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Nothing is waiting for that approval.");
        }

        if (!await MayApproveAsync(room.BrandId, gate, member, cancellationToken))
        {
            return MoveResult.Failed(MoveError.NotApprover, "You aren't an approver for this step.");
        }

        var before = Snapshot(room);
        ClearGate(room);
        await EnterAsync(room, next, cancellationToken);
        await notifications.ResolveAsync(GateSubject(room.Id, gate), cancellationToken);
        await FinishAsync(room, new AuditRecord("campaign_room.approved", AuditTarget, room.Id.ToString(), before, After(room, new { gate, comment = Trimmed(comment) })), cancellationToken);
        return MoveResult.Done(room);
    }

    public async Task<MoveResult> SendBackAsync(Guid roomId, string to, string comment, int version, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, version, cancellationToken) is not { Room: { } room } opened)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (opened.Error != MoveError.None)
        {
            return opened;
        }

        if (!RoomLifecycle.SendBackTargets(room.State, room.PendingGate).Contains(to))
        {
            return MoveResult.Failed(MoveError.NotAllowed, "This campaign can't be sent back to that stage.");
        }

        var gate = room.PendingGate ?? RoomLifecycle.GateFor(room.State);
        if (gate is null || !await MayApproveAsync(room.BrandId, gate, member, cancellationToken))
        {
            return MoveResult.Failed(MoveError.NotApprover, "You aren't an approver for this step.");
        }

        var before = Snapshot(room);
        var from = room.State;
        if (room.PendingGate is { } pending)
        {
            await notifications.ResolveAsync(GateSubject(room.Id, pending), cancellationToken);
        }

        ClearGate(room);
        room.LastSendBack = new RoomSendBack { From = from, To = to, Comment = comment, By = member.UserId, At = time.GetUtcNow() };
        await EnterAsync(room, to, cancellationToken);
        var brand = await BrandAsync(room.BrandId, cancellationToken);
        await notifications.NotifyAsync(
            new Notification(
                ReturnedKind,
                LiveAudience.Users(room.OwnerId),
                $"{room.Title} was sent back",
                comment,
                Link(brand, room.Id)),
            cancellationToken);
        await FinishAsync(room, new AuditRecord("campaign_room.sent_back", AuditTarget, room.Id.ToString(), before, After(room, new { from, to, comment })), cancellationToken);
        return MoveResult.Done(room);
    }

    public async Task<MoveResult> DiscardAsync(Guid roomId, string? reason, int version, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, version, cancellationToken) is not { Room: { } room } opened)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (opened.Error != MoveError.None)
        {
            return opened;
        }

        if (!RoomLifecycle.CanDiscard(room.State))
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Only a campaign that hasn't launched can be discarded.");
        }

        var before = Snapshot(room);
        if (room.PendingGate is { } pending)
        {
            await notifications.ResolveAsync(GateSubject(room.Id, pending), cancellationToken);
        }

        ClearGate(room);
        room.DiscardReason = Trimmed(reason);
        room.DiscardedFrom = room.State;
        await EnterAsync(room, RoomStates.Discarded, cancellationToken);
        await FinishAsync(room, new AuditRecord("campaign_room.discarded", AuditTarget, room.Id.ToString(), before, After(room, new { reason = room.DiscardReason })), cancellationToken);
        return MoveResult.Done(room);
    }

    public async Task<MoveResult> AdvanceAsync(Guid roomId, string signal, AuditActor actor, CancellationToken cancellationToken)
    {
        if (RoomLifecycle.Signal(signal) is not { } move)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Unknown signal.");
        }

        if (await LockedAsync(roomId, cancellationToken) is not { } room)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (room.State != move.From)
        {
            return MoveResult.Failed(MoveError.NotAllowed, $"The campaign is in {room.State}, not {move.From}.");
        }

        var before = Snapshot(room);
        if (move.To != move.From)
        {
            await EnterAsync(room, move.To, cancellationToken);
        }
        else
        {
            Touch(room);
        }

        await FinishAsync(room, new AuditRecord("campaign_room.advanced", AuditTarget, room.Id.ToString(), before, After(room, new { signal }), actor), cancellationToken);
        return MoveResult.Done(room);
    }

    public void Touch(Room room)
    {
        room.Version++;
        room.UpdatedAt = time.GetUtcNow();
        room.Status = RoomLifecycle.StatusOf(room.State, room.PendingGate, room.RunStatus);
    }

    public async Task PublishAsync(Room room, CancellationToken cancellationToken)
    {
        await live.PublishAsync(CampaignLiveEvents.Changed(room.Id), cancellationToken);
        await live.PublishAsync(CampaignLiveEvents.ListChanged(), cancellationToken);
    }

    public static string Link(BrandSummary brand, Guid roomId) => $"/{brand.Handle}/campaigns/{roomId}";

    private async Task<MoveResult?> OpenAsync(Guid roomId, int version, CancellationToken cancellationToken)
    {
        if (await LockedAsync(roomId, cancellationToken) is not { } room)
        {
            return null;
        }

        return room.Version != version
            ? new MoveResult(room, MoveError.Stale, "This campaign changed since you opened it. Refresh and try again.")
            : MoveResult.Done(room);
    }

    private async Task EnterAsync(Room room, string state, CancellationToken cancellationToken)
    {
        var leaving = room.State;
        room.State = state;
        room.StateEnteredAt = time.GetUtcNow();
        room.RunStatus = null;
        await CancelRunsAsync(room.Id, leaving, cancellationToken);
        if (state == RoomStates.AssetReview)
        {
            await RequestGateAsync(room, RoomGates.Assets, null, cancellationToken);
        }

        Touch(room);
    }

    private async Task CancelRunsAsync(Guid roomId, string stage, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.StageRuns
            .Where(run => run.RoomId == roomId && run.Stage == stage && (run.Status == StageRunStatuses.Queued || run.Status == StageRunStatuses.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, StageRunStatuses.Cancelled)
                    .SetProperty(run => run.FinishedAt, now)
                    .SetProperty(run => run.ClaimedUntil, (DateTimeOffset?)null),
                cancellationToken);
    }

    private async Task RequestGateAsync(Room room, string gate, Guid? requestedBy, CancellationToken cancellationToken)
    {
        room.PendingGate = gate;
        room.GateRequestedBy = requestedBy;
        room.GateRequestedAt = time.GetUtcNow();
        var brand = await BrandAsync(room.BrandId, cancellationToken);
        var assigned = await approvers.ForAsync(room.BrandId, DutyOf(gate), cancellationToken);
        var audience = assigned.Count > 0
            ? LiveAudience.Users([.. assigned.Select(approver => approver.UserId)])
            : LiveAudience.Roles(BrandRoles.Approver);
        await notifications.NotifyAsync(
            new Notification(
                ApprovalKind,
                audience,
                $"{GateTitle(gate)} for {room.Title}",
                GateBody(gate),
                Link(brand, room.Id),
                GateSubject(room.Id, gate)),
            cancellationToken);
    }

    private static void ClearGate(Room room)
    {
        room.PendingGate = null;
        room.GateRequestedBy = null;
        room.GateRequestedAt = null;
    }

    private async Task FinishAsync(Room room, AuditRecord record, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(record, cancellationToken);
        await PublishAsync(room, cancellationToken);
    }

    private static object After(Room room, object details) => new { room.State, room.Status, room.PendingGate, details };

    private async Task<BrandSummary> BrandAsync(Guid brandId, CancellationToken cancellationToken) =>
        (await brands.GetAsync([brandId], cancellationToken)).Single();

    private static object Snapshot(Room room) => new { room.State, room.Status, room.PendingGate };

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static string GateTitle(string gate) => gate switch
    {
        RoomGates.Brief => "Brief approval",
        RoomGates.Assets => "Asset approval",
        _ => "Launch approval",
    };

    private static string GateBody(string gate) => gate switch
    {
        RoomGates.Brief => "A brief is ready for your approval.",
        RoomGates.Assets => "The assets are checked and ready for your approval.",
        _ => "A campaign is ready to launch and needs your approval.",
    };
}
