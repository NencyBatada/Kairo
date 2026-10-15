namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class Brief : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public int Version { get; set; }

    public BriefContent Content { get; set; } = new();

    public string Origin { get; set; } = BriefOrigins.Starter;

    public Guid? StageRunId { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class BriefContent
{
    public string Title { get; set; } = "";

    public string Goal { get; set; } = "";

    public string Audience { get; set; } = "";

    public string KeyMessage { get; set; } = "";

    public string? Offer { get; set; }

    public List<string> Regions { get; set; } = [];

    public List<string> ProductIds { get; set; } = [];

    public List<string> Channels { get; set; } = [];

    public string Timing { get; set; } = "";
}

internal static class BriefOrigins
{
    public const string Starter = "starter";
    public const string Trigger = "trigger";
}
