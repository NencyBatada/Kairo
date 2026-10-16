using System.Text.Json;
using Kairo.Contracts.Identity;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.Modules.CampaignRooms.LaunchPlans;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class CampaignReader(
    CampaignRoomsDbContext db,
    RoomWorkflow workflow,
    BriefGate briefGate,
    AssetApproval assetApproval,
    LaunchPlanDesk launchPlans,
    IBrandMembers members,
    CurrentMember member)
{
    public const int MaxRuns = 20;
    public const string FormerMember = "Former member";

    public async Task<IReadOnlyList<BrandMember>> MembersAsync(CancellationToken cancellationToken) =>
        await members.ListAsync(cancellationToken);

    public static CampaignPersonView Person(Guid id, IReadOnlyList<BrandMember> people) =>
        new(id, people.FirstOrDefault(person => person.UserId == id)?.Name ?? FormerMember);

    public static CampaignCard Card(Room room, IReadOnlyList<BrandMember> people) => new(
        room.Id,
        room.Title,
        room.State,
        room.Status,
        Person(room.OwnerId, people),
        room.PendingGate,
        [.. room.Products.Select(product => new CampaignProductView(product.Id, product.Title))],
        room.CreatedAt,
        room.StateEnteredAt,
        room.UpdatedAt);

    public async Task<CampaignDetail> DetailAsync(Room room, CancellationToken cancellationToken)
    {
        var people = await MembersAsync(cancellationToken);
        var brief = await db.Briefs.AsNoTracking()
            .Where(row => row.RoomId == room.Id)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var runs = await db.StageRuns.AsNoTracking()
            .Where(run => run.RoomId == room.Id)
            .OrderByDescending(run => run.QueuedAt)
            .Take(MaxRuns)
            .ToListAsync(cancellationToken);
        var latest = runs.FirstOrDefault(run => run.Stage == room.State && run.Status != StageRunStatuses.Cancelled);
        CampaignApprovalView? approval = null;
        if (room.PendingGate is { } gate)
        {
            var route = await workflow.RouteAsync(room, gate, cancellationToken);
            var approvers = await workflow.ApproversAsync(room.BrandId, gate, route.Route, people, cancellationToken);
            approval = new CampaignApprovalView(
                gate,
                RoomWorkflow.GateTitle(gate),
                [.. approvers.Select(approver => new CampaignPersonView(approver.UserId, approver.Name))],
                room.GateRequestedBy is { } requester ? Person(requester, people) : null,
                room.GateRequestedAt ?? room.UpdatedAt,
                route.Route,
                route.Reason);
        }

        return new CampaignDetail(
            room.Id,
            room.Title,
            room.State,
            room.Status,
            room.Version,
            Person(room.OwnerId, people),
            room.OpportunityId,
            room.CreatedAt,
            room.UpdatedAt,
            Tracker(room, latest, approval, Person(room.OwnerId, people).Name),
            approval,
            room.LastSendBack is { } sent ? new CampaignSendBackView(sent.From, sent.To, sent.Comment, Person(sent.By, people), sent.At) : null,
            room.DiscardReason,
            JsonSerializer.Deserialize<JsonElement>(room.Evidence),
            brief is null ? null : BriefView(brief),
            [.. runs.Select(RunView)],
            await ActionsAsync(room, brief is not null, latest, cancellationToken));
    }

    public static CampaignTrackerView Tracker(Room room, StageRun? latest, CampaignApprovalView? approval, string ownerName)
    {
        var reached = room.State == RoomStates.Discarded ? RoomStates.Order(room.DiscardedFrom ?? RoomStates.BriefDraft) : RoomStates.Order(room.State);
        var steps = RoomStates.Stages
            .Select((stage, index) => new CampaignStageStepView(
                stage,
                StageLabel(stage),
                index < reached || room.State == RoomStates.Closed ? "done" : index == reached ? "current" : "upcoming"))
            .ToArray();
        var (kind, owner, next) = Owner(room, latest, approval, ownerName);
        return new CampaignTrackerView(steps, room.State, kind, owner, next, room.StateEnteredAt);
    }

    public static string StageLabel(string stage) => stage switch
    {
        RoomStates.BriefDraft => "Brief draft",
        RoomStates.Creating => "Creating",
        RoomStates.AssetReview => "Asset review",
        RoomStates.ReadyToLaunch => "Ready to launch",
        RoomStates.Launching => "Launching",
        RoomStates.Live => "Live",
        RoomStates.ResultsReady => "Results ready",
        RoomStates.Closed => "Closed",
        _ => "Discarded",
    };

    private static (string Kind, string Owner, string Next) Owner(Room room, StageRun? latest, CampaignApprovalView? approval, string ownerName)
    {
        if (room.State == RoomStates.Discarded)
        {
            return ("none", "", "Nothing to do. This campaign was discarded.");
        }

        if (room.State == RoomStates.Closed)
        {
            return ("none", "", "Nothing left to do. The learnings are saved.");
        }

        if (room.State == RoomStates.Launching)
        {
            return room.RunStatus == StageRunStatuses.Failed
                ? ("person", ownerName, "The launch needs attention. Open the launch progress to see the cause and suggested fix.")
                : ("agent", "Launch service", "Kairo is building the approved campaigns in Google Ads.");
        }

        if (latest is { } run && StageRunStatuses.IsActive(run.Status))
        {
            return ("agent", StageProgress.ActorName(run.Agent), Working(run));
        }

        if (latest is { Status: StageRunStatuses.Failed } failed)
        {
            return ("person", ownerName, $"The {StageProgress.ActorName(failed.Agent)} couldn't finish. Try it again.");
        }

        if (approval is not null)
        {
            var names = approval.Approvers.Length == 0 ? "An approver" : string.Join(", ", approval.Approvers.Select(approver => approver.Name));
            return ("approvers", names, $"{names} to review the {GateObject(approval.Gate)} and approve it or send it back.");
        }

        return room.State switch
        {
            RoomStates.BriefDraft => ("person", ownerName, "Review the brief and ask for approval."),
            RoomStates.Creating => ("person", ownerName, "Review the checked assets and request asset approval."),
            RoomStates.AssetReview => ("person", ownerName, "Review the assets."),
            RoomStates.ReadyToLaunch => ("person", ownerName, "Check the launch summary and ask for launch approval."),
            RoomStates.Launching => ("agent", "Kairo", "Kairo is building the campaign in Google Ads."),
            RoomStates.Live => ("person", ownerName, "Watch performance until the test window ends."),
            _ => ("person", ownerName, "Read the results and save what was learned."),
        };
    }

    private static string Working(StageRun run) => run.Kind switch
    {
        StageKinds.Brief or StageKinds.StarterBrief => "The Plan agent is drafting the brief.",
        StageKinds.CreateCopy => "The Create agent is writing the ads.",
        StageKinds.AssetRegenerate => "The Create agent is rewriting an asset.",
        StageKinds.Check => "Brand Guardian is checking the assets.",
        StageKinds.CheckFix => "Brand Guardian is fixing assets that failed a check.",
        _ => $"The {StageProgress.ActorName(run.Agent)} is working on this stage.",
    };

    private static string GateObject(string gate) => gate switch
    {
        RoomGates.Brief => "brief",
        RoomGates.Assets => "assets",
        _ => "launch",
    };

    private async Task<CampaignActionsView> ActionsAsync(Room room, bool hasBrief, StageRun? latest, CancellationToken cancellationToken)
    {
        var marketer = member.Has(BrandRoles.Marketer);
        var gate = room.PendingGate ?? RoomLifecycle.GateFor(room.State);
        var route = room.PendingGate is { } pending ? await workflow.RouteAsync(room, pending, cancellationToken) : BriefRoute.Approvers;
        var mayApprove = gate is not null && await workflow.MayApproveAsync(room.BrandId, gate, route.Route, member, cancellationToken);
        var maySendBack = gate is not null && (mayApprove || await workflow.MaySendBackAsync(room, gate, member, cancellationToken));
        var stateGate = RoomLifecycle.RequestFrom(room.State);
        var canRequest = marketer
            && stateGate is not null
            && room.PendingGate is null
            && (stateGate == RoomGates.Assets || room.Status != RoomStatuses.AgentWorking)
            && (stateGate != RoomGates.Brief || hasBrief);
        var blocker = !canRequest ? null : stateGate switch
        {
            RoomGates.Brief => (await briefGate.CheckAsync(room, cancellationToken)).Problem,
            RoomGates.Assets => await assetApproval.RequestBlockerAsync(room, cancellationToken),
            RoomGates.Launch => await launchPlans.BlockerAsync(room.Id, cancellationToken),
            _ => null,
        };
        var approveBlocker = room.PendingGate == RoomGates.Assets && mayApprove ? await assetApproval.BlockerAsync(room.Id, cancellationToken) : null;
        var requestCheck = marketer
            && CheckReview.CheckBlocker(room) is null
            && await db.Assets.AnyAsync(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden, cancellationToken);
        return new CampaignActionsView(
            canRequest && blocker is null ? stateGate : null,
            room.PendingGate is not null && mayApprove && approveBlocker is null ? room.PendingGate : null,
            maySendBack ? [.. RoomLifecycle.SendBackTargets(room.State, room.PendingGate)] : [],
            marketer && RoomLifecycle.CanDiscard(room.State),
            marketer && latest is { Status: StageRunStatuses.Failed } failed ? failed.Id : null,
            blocker,
            marketer && latest is { Status: StageRunStatuses.Failed, Kind: StageKinds.CreateVideo },
            approveBlocker,
            requestCheck);
    }

    public static CampaignBriefView BriefView(Brief brief)
    {
        var content = brief.Content.Normalized();
        var timing = content.StartDate is { } start && content.EndDate is { } end
            ? $"{BriefChecks.Day(start)} to {BriefChecks.Day(end)}"
            : content.Timing ?? "";
        return new CampaignBriefView(
            brief.Version,
            brief.Origin,
            content.Title,
            content.Objective,
            string.Join("; ", content.Audiences ?? []),
            content.KeyMessages?.FirstOrDefault() ?? "",
            content.Offer,
            [.. content.Regions ?? []],
            [.. content.ProductIds ?? []],
            [.. content.CampaignTypes ?? []],
            timing,
            brief.Model,
            brief.PromptVersion,
            brief.CreatedAt);
    }

    private static CampaignRunView RunView(StageRun run) => new(
        run.Id,
        run.Stage,
        run.Agent,
        run.Kind,
        run.Status,
        run.Attempts,
        run.QueuedAt,
        run.StartedAt,
        run.FinishedAt,
        run.Model,
        run.PromptVersion,
        run.InputTokens,
        run.OutputTokens,
        run.ErrorMessage);
}
