using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.Contracts.Approvals;
using Kairo.Modules.CampaignRooms.LaunchPlans;

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
    BriefGate briefGate,
    IBrandDirectory brands,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    StageQueue queue,
    AssetApproval assetApproval,
    ApprovalSnapshots snapshots,
    IApprovalLocks approvalLocks,
    LaunchPlanDesk launchPlans,
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

    public async Task<BriefRoute> RouteAsync(Room room, string gate, CancellationToken cancellationToken) =>
        gate is RoomGates.Brief or RoomGates.Launch ? await briefGate.RouteAsync(room, cancellationToken) : BriefRoute.Approvers;

    public async Task<IReadOnlyList<BrandMember>> ApproversAsync(Guid brandId, string gate, string route, IReadOnlyList<BrandMember> members, CancellationToken cancellationToken)
    {
        if (route == ApprovalRoutes.BudgetOwner)
        {
            return await approvers.ForAsync(brandId, ApprovalDuty.BudgetOwner, cancellationToken);
        }

        var assigned = await approvers.ForAsync(brandId, DutyOf(gate), cancellationToken);
        return assigned.Count > 0 ? assigned : members.Where(member => (member.Roles & BrandRoles.Approver) != BrandRoles.None).ToList();
    }

    public async Task<bool> MayApproveAsync(Guid brandId, string gate, string route, CurrentMember member, CancellationToken cancellationToken)
    {
        if (!member.Has(BrandRoles.Approver))
        {
            return false;
        }

        if (route == ApprovalRoutes.BudgetOwner)
        {
            var owners = await approvers.ForAsync(brandId, ApprovalDuty.BudgetOwner, cancellationToken);
            return owners.Any(owner => owner.UserId == member.UserId);
        }

        var assigned = await approvers.ForAsync(brandId, DutyOf(gate), cancellationToken);
        return assigned.Count == 0 || assigned.Any(approver => approver.UserId == member.UserId);
    }

    public async Task<bool> MaySendBackAsync(Room room, string gate, CurrentMember member, CancellationToken cancellationToken)
    {
        if (room.PendingGate != RoomGates.Brief || gate != RoomGates.Brief)
        {
            return await MayApproveAsync(room.BrandId, gate, ApprovalRoutes.Approvers, member, cancellationToken);
        }

        var route = await RouteAsync(room, gate, cancellationToken);
        return await MayApproveAsync(room.BrandId, gate, route.Route, member, cancellationToken)
            || (room.GateRoute is { } requested && requested != route.Route && await MayApproveAsync(room.BrandId, gate, requested, member, cancellationToken));
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

        if (!RoomLifecycle.MarketerRequests(gate) || RoomLifecycle.RequestFrom(room.State) != gate)
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

        BriefGateCheck? check = null;
        if (gate == RoomGates.Brief)
        {
            check = await briefGate.CheckAsync(room, cancellationToken);
            if (check.Problem is { } problem)
            {
                return MoveResult.Failed(MoveError.NotAllowed, problem);
            }
        }

        var before = Snapshot(room);
        if (gate == RoomGates.Assets)
        {
            if (await assetApproval.RequestBlockerAsync(room, cancellationToken) is { } blocker)
            {
                return MoveResult.Failed(MoveError.NotAllowed, blocker);
            }

            await EnterAsync(room, RoomStates.AssetReview, cancellationToken, requestedBy: member.UserId);
            await FinishAsync(room, new AuditRecord("campaign_room.approval_requested", AuditTarget, room.Id.ToString(), before, After(room, new { gate, from = RoomStates.Creating })), cancellationToken);
            return MoveResult.Done(room);
        }

        if (gate == RoomGates.Launch && await launchPlans.BlockerAsync(room.Id, cancellationToken) is { } launchBlocker)
        {
            return MoveResult.Failed(MoveError.NotAllowed, launchBlocker);
        }

        var requestedRoute = gate == RoomGates.Launch ? await briefGate.RouteAsync(room, cancellationToken) : check?.Route ?? BriefRoute.Approvers;
        if (requestedRoute.ToBudgetOwner && (await approvers.ForAsync(room.BrandId, ApprovalDuty.BudgetOwner, cancellationToken)).Count == 0)
        {
            return MoveResult.Failed(MoveError.NotAllowed, BriefGate.NoBudgetOwner);
        }

        room.GateRoute = requestedRoute.Route;
        room.GateBriefVersion = check?.BriefVersion;
        room.GatePlanVersion = check?.PlanVersion;
        await RequestGateAsync(room, gate, member.UserId, requestedRoute, cancellationToken);
        Touch(room);
        var details = check is null
            ? (object)new { gate }
            : new { gate, route = check.Route.Route, briefVersion = check.BriefVersion, proofPlanVersion = check.PlanVersion };
        await FinishAsync(room, new AuditRecord("campaign_room.approval_requested", AuditTarget, room.Id.ToString(), before, After(room, details)), cancellationToken);
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

        ProofPlan? plan = null;
        var route = BriefRoute.Approvers;
        if (gate == RoomGates.Brief)
        {
            var latestBrief = await db.Briefs.Where(brief => brief.RoomId == room.Id).MaxAsync(brief => (int?)brief.Version, cancellationToken);
            plan = await db.ProofPlans.Where(row => row.RoomId == room.Id).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
            if (room.GateBriefVersion is not null && (latestBrief != room.GateBriefVersion || plan?.Version != room.GatePlanVersion))
            {
                return MoveResult.Failed(MoveError.NotAllowed, "The brief or its test plan changed after approval was requested. Send it back so it can be asked for again.");
            }

            route = await briefGate.RouteAsync(room, cancellationToken);
        }

        if (gate == RoomGates.Launch)
        {
            route = await briefGate.RouteAsync(room, cancellationToken);
        }

        if (!await MayApproveAsync(room.BrandId, gate, route.Route, member, cancellationToken))
        {
            return MoveResult.Failed(MoveError.NotApprover, NotApproverMessage(room, route));
        }

        var approvedSnapshot = await GateSnapshotAsync(room.Id, gate, cancellationToken);
        if (room.GateHash is null || room.GateHash != ApprovalHash.Of(approvedSnapshot))
        {
            return MoveResult.Failed(MoveError.NotAllowed, "The plan changed or has no approval snapshot. Send it back and request approval again.");
        }

        if (gate == RoomGates.Launch && await launchPlans.BlockerAsync(room.Id, cancellationToken) is { } launchBlocker)
        {
            return MoveResult.Failed(MoveError.NotAllowed, launchBlocker);
        }

        ApprovedAssets? assets = null;
        if (gate == RoomGates.Assets)
        {
            assets = await assetApproval.ApproveAsync(room.Id, member.UserId, cancellationToken);
            if (assets.Problem is { } problem)
            {
                return MoveResult.Failed(MoveError.NotAllowed, problem);
            }
        }

        var before = Snapshot(room);
        var briefVersion = room.GateBriefVersion;
        var withWarnings = plan is not null && plan.Read().Warnings.Count > 0;
        if (withWarnings)
        {
            plan!.ApprovedWithWarnings = true;
        }

        ClearGate(room);
        await approvalLocks.ApproveAsync(room.Id, gate, member.UserId, approvedSnapshot, cancellationToken);
        await EnterAsync(room, next, cancellationToken);
        var locked = next == RoomStates.Launching ? await LockPlanAsync(room.Id, cancellationToken) : null;
        await notifications.ResolveAsync(GateSubject(room.Id, gate), cancellationToken);
        var details = gate switch
        {
            RoomGates.Brief => (object)new { gate, comment = Trimmed(comment), route = route.Route, briefVersion, proofPlanVersion = plan?.Version, approvedWithWarnings = withWarnings },
            RoomGates.Assets => new { gate, comment = Trimmed(comment), assetsApproved = assets?.Approved, mediaKept = assets?.Kept },
            _ => new { gate, comment = Trimmed(comment) },
        };
        await FinishAsync(room, new AuditRecord("campaign_room.approved", AuditTarget, room.Id.ToString(), before, After(room, details)), cancellationToken);
        if (locked is not null)
        {
            await audit.RecordAsync(
                new AuditRecord("proof_plan.locked", AuditTarget, room.Id.ToString(), null, new { version = locked.Version, lockedAt = locked.LockedAt }),
                cancellationToken);
        }

        return MoveResult.Done(room);
    }

    private static string NotApproverMessage(Room room, BriefRoute route) => route switch
    {
        { ToBudgetOwner: true } when room.GateRoute != ApprovalRoutes.BudgetOwner =>
            "The budget limit changed after approval was requested, so only a budget owner can approve this brief now.",
        { ToBudgetOwner: true } => "Only a budget owner can approve this brief, because its budget is above the limit.",
        _ => "You aren't an approver for this step.",
    };

    private async Task<ProofPlan?> LockPlanAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var plan = await db.ProofPlans.Where(row => row.RoomId == roomId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        if (plan is null || plan.LockedAt is not null)
        {
            return null;
        }

        plan.LockedAt = time.GetUtcNow();
        return plan;
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
        if (gate is null || !await MaySendBackAsync(room, gate, member, cancellationToken))
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

    public async Task<MoveResult> SendBackAssetsAsync(Guid roomId, IReadOnlyCollection<Guid> assetIds, string comment, int version, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, version, cancellationToken) is not { Room: { } room } opened)
        {
            return MoveResult.Failed(MoveError.NotFound);
        }

        if (opened.Error != MoveError.None)
        {
            return opened;
        }

        if (room.State != RoomStates.AssetReview || room.PendingGate != RoomGates.Assets)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Assets can be sent back only while they wait for approval.");
        }

        if (!await MayApproveAsync(room.BrandId, RoomGates.Assets, ApprovalRoutes.Approvers, member, cancellationToken))
        {
            return MoveResult.Failed(MoveError.NotApprover, "You aren't an approver for this step.");
        }

        var ids = assetIds.Distinct().ToList();
        var assets = await db.Assets
            .Where(asset => asset.RoomId == room.Id && ids.Contains(asset.Id) && asset.Status == AssetStatuses.Active && !asset.Hidden)
            .ToListAsync(cancellationToken);
        if (assets.Count != ids.Count)
        {
            return MoveResult.Failed(MoveError.NotAllowed, "Some of those assets aren't in this campaign any more. Refresh and try again.");
        }

        var before = Snapshot(room);
        await notifications.ResolveAsync(GateSubject(room.Id, RoomGates.Assets), cancellationToken);
        ClearGate(room);
        room.LastSendBack = new RoomSendBack { From = room.State, To = RoomStates.Creating, Comment = comment, By = member.UserId, At = time.GetUtcNow() };
        await EnterAsync(room, RoomStates.Creating, cancellationToken, startCreate: false);
        var redo = assets
            .Where(asset => !asset.Locked)
            .OrderBy(asset => AssetReworker.Order(asset.Kind))
            .ThenBy(asset => asset.Key, StringComparer.Ordinal)
            .Select(asset => new RegenerationInput(asset.Id, asset.CurrentVersion, comment, asset.Kind))
            .ToList();
        if (redo.Count > 0)
        {
            await queue.EnqueueAsync(room, StageKinds.AssetRegenerate, member.UserId, cancellationToken, AssetRegenerateWork.BatchInput(redo));
            Touch(room);
        }

        var brand = await BrandAsync(room.BrandId, cancellationToken);
        await notifications.NotifyAsync(
            new Notification(
                ReturnedKind,
                LiveAudience.Users(room.OwnerId),
                $"Assets in {room.Title} were sent back",
                comment,
                Link(brand, room.Id)),
            cancellationToken);
        var details = new
        {
            from = RoomStates.AssetReview,
            to = RoomStates.Creating,
            comment,
            assets = assets.Select(asset => asset.Id).ToArray(),
            regenerating = redo.Select(item => item.AssetId).ToArray(),
        };
        await FinishAsync(room, new AuditRecord("campaign_room.sent_back", AuditTarget, room.Id.ToString(), before, After(room, details)), cancellationToken);
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

    private async Task EnterAsync(Room room, string state, CancellationToken cancellationToken, bool startCreate = true, Guid? requestedBy = null)
    {
        var invalidates = state switch
        {
            RoomStates.BriefDraft => RoomGates.All,
            RoomStates.Creating or RoomStates.AssetReview => new[] { RoomGates.Assets, RoomGates.Launch },
            RoomStates.ReadyToLaunch => new[] { RoomGates.Launch },
            _ => Array.Empty<string>(),
        };
        if (invalidates.Length > 0) await approvalLocks.InvalidateAsync(room.Id, invalidates, cancellationToken);
        var leaving = room.State;
        room.State = state;
        room.StateEnteredAt = time.GetUtcNow();
        room.RunStatus = null;
        await CancelRunsAsync(room.Id, leaving, cancellationToken);
        if (state == RoomStates.AssetReview)
        {
            await RequestGateAsync(room, RoomGates.Assets, requestedBy, BriefRoute.Approvers, cancellationToken);
        }

        if (state == RoomStates.Creating && startCreate && queue.Find(StageKinds.CreateCopy) is not null)
        {
            await queue.EnqueueAsync(room, StageKinds.CreateCopy, null, cancellationToken);
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

    private async Task RequestGateAsync(Room room, string gate, Guid? requestedBy, BriefRoute route, CancellationToken cancellationToken)
    {
        room.GateHash = ApprovalHash.Of(await GateSnapshotAsync(room.Id, gate, cancellationToken));
        room.PendingGate = gate;
        room.GateRequestedBy = requestedBy;
        room.GateRequestedAt = time.GetUtcNow();
        var brand = await BrandAsync(room.BrandId, cancellationToken);
        var assigned = await approvers.ForAsync(room.BrandId, route.ToBudgetOwner ? ApprovalDuty.BudgetOwner : DutyOf(gate), cancellationToken);
        var audience = assigned.Count > 0
            ? LiveAudience.Users([.. assigned.Select(approver => approver.UserId)])
            : LiveAudience.Roles(BrandRoles.Approver);
        await notifications.NotifyAsync(
            new Notification(
                ApprovalKind,
                audience,
                $"{GateTitle(gate)} for {room.Title}",
                route.ToBudgetOwner ? "A brief above the budget limit is ready for your approval as budget owner." : GateBody(gate),
                Link(brand, room.Id),
                GateSubject(room.Id, gate)),
            cancellationToken);
    }

    private static void ClearGate(Room room)
    {
        room.GateHash = null;
        room.PendingGate = null;
        room.GateRequestedBy = null;
        room.GateRequestedAt = null;
        room.GateRoute = null;
        room.GateBriefVersion = null;
        room.GatePlanVersion = null;
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

    private async Task<string> GateSnapshotAsync(Guid roomId, string gate, CancellationToken cancellationToken) => gate switch
    {
        RoomGates.Brief => await snapshots.BriefAsync(roomId, cancellationToken),
        RoomGates.Assets => await snapshots.AssetsSnapshotAsync(roomId, cancellationToken),
        _ => await launchPlans.GetAsync(roomId, cancellationToken) is { } plan ? LaunchPlanDesk.Serialize(plan) : "{}",
    };

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
