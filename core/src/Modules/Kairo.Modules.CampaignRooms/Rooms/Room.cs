namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class Room : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Title { get; set; } = "";

    public string State { get; set; } = RoomStates.BriefDraft;

    public Guid OpportunityId { get; set; }

    public string Evidence { get; set; } = "{}";

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal static class RoomStates
{
    public const string BriefDraft = "brief_draft";
}
