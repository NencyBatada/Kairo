namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class Room : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Title { get; set; } = "";

    public string State { get; set; } = RoomStates.BriefDraft;

    public string Status { get; set; } = RoomStatuses.InProgress;

    public int Version { get; set; }

    public Guid OpportunityId { get; set; }

    public string Evidence { get; set; } = "{}";

    public List<RoomProduct> Products { get; set; } = [];

    public Guid OwnerId { get; set; }

    public string? RunStatus { get; set; }

    public string? PendingGate { get; set; }

    public Guid? GateRequestedBy { get; set; }

    public DateTimeOffset? GateRequestedAt { get; set; }

    public string? GateRoute { get; set; }

    public string? GateHash { get; set; }

    public int? GateBriefVersion { get; set; }

    public int? GatePlanVersion { get; set; }

    public RoomSendBack? LastSendBack { get; set; }

    public string? DiscardReason { get; set; }

    public string? DiscardedFrom { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset StateEnteredAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class RoomProduct
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";
}

internal sealed class RoomSendBack
{
    public string From { get; set; } = "";

    public string To { get; set; } = "";

    public string Comment { get; set; } = "";

    public Guid By { get; set; }

    public DateTimeOffset At { get; set; }
}
