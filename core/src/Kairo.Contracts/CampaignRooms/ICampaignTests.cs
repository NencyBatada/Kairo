namespace Kairo.Contracts.CampaignRooms;

public interface ICampaignTests
{
    Task<IReadOnlyList<CampaignTest>> ListRunningAsync(CancellationToken cancellationToken);

    Task<CampaignTest?> GetAsync(Guid roomId, CancellationToken cancellationToken);
}

public static class CampaignTestStages
{
    public const string Live = "live";
    public const string ResultsReady = "results_ready";
    public const string Closed = "closed";
}

public sealed record CampaignTest(
    Guid RoomId,
    string Title,
    string Stage,
    string Kpi,
    DateOnly? Start,
    DateOnly? End,
    DateTimeOffset? EndedAt,
    bool EndedEarly)
{
    public bool Running => Stage == CampaignTestStages.Live;
}
