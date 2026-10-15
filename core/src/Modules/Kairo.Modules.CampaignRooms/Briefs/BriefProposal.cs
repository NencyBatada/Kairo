namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class BriefProposal : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public int BriefVersion { get; set; }

    public string Instruction { get; set; } = "";

    public BriefContent Content { get; set; } = new();

    public List<string> Fields { get; set; } = [];

    public string Summary { get; set; } = "";

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
}
