using System.Text.Json;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed record CampaignPersonView(Guid Id, string Name);

internal sealed record CampaignProductView(string Id, string Title);

internal sealed record CampaignCard(
    Guid Id,
    string Title,
    string Stage,
    string Status,
    CampaignPersonView Owner,
    string? PendingGate,
    CampaignProductView[] Products,
    DateTimeOffset OpenedAt,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset UpdatedAt);

internal sealed record CampaignPage(CampaignCard[] Campaigns, string? NextBefore);

internal sealed record CampaignStageStepView(string Stage, string Label, string Progress);

internal sealed record CampaignTrackerView(
    CampaignStageStepView[] Stages,
    string Current,
    string OwnerKind,
    string Owner,
    string NextAction,
    DateTimeOffset EnteredAt);

internal sealed record CampaignApprovalView(
    string Gate,
    string Label,
    CampaignPersonView[] Approvers,
    CampaignPersonView? RequestedBy,
    DateTimeOffset RequestedAt,
    string Route,
    string? RouteReason);

internal sealed record CampaignSendBackView(string From, string To, string Comment, CampaignPersonView By, DateTimeOffset At);

internal sealed record CampaignBriefView(
    int Version,
    string Origin,
    string Title,
    string Goal,
    string Audience,
    string KeyMessage,
    string? Offer,
    string[] Regions,
    string[] ProductIds,
    string[] Channels,
    string Timing,
    string? Model,
    string? PromptVersion,
    DateTimeOffset CreatedAt);

internal sealed record CampaignRunView(
    Guid Id,
    string Stage,
    string Agent,
    string Kind,
    string Status,
    int Attempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Model,
    string? PromptVersion,
    int? InputTokens,
    int? OutputTokens,
    string? Error);

internal sealed record CampaignActionsView(
    string? RequestApproval,
    string? Approve,
    string[] SendBackTo,
    bool Discard,
    Guid? RetryRun,
    string? RequestApprovalBlocker,
    bool ContinueWithoutVideo);

internal sealed record CampaignDetail(
    Guid Id,
    string Title,
    string Stage,
    string Status,
    int Version,
    CampaignPersonView Owner,
    Guid OpportunityId,
    DateTimeOffset OpenedAt,
    DateTimeOffset UpdatedAt,
    CampaignTrackerView Tracker,
    CampaignApprovalView? PendingApproval,
    CampaignSendBackView? LastSendBack,
    string? DiscardReason,
    JsonElement Evidence,
    CampaignBriefView? Brief,
    CampaignRunView[] Runs,
    CampaignActionsView Actions);

internal sealed record CampaignActivityView(
    long Id,
    DateTimeOffset OccurredAt,
    string ActorType,
    string ActorName,
    string Action,
    string Label,
    JsonElement? Details);

internal sealed record CampaignActivityPage(CampaignActivityView[] Entries, string? NextBefore);

internal sealed record ApprovalRequest(string? Gate, int? Version);

internal sealed record ApproveRequest(string? Gate, int? Version, string? Comment);

internal sealed record SendBackRequest(string? To, string? Comment, int? Version);

internal sealed record DiscardRequest(string? Reason, int? Version);
