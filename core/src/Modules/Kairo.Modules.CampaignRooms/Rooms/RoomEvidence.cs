using System.Text.Json;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed record EvidenceRegion(string? Id, string? Name, long? LastMonthSearches = null, double? Growth = null);

internal sealed record EvidenceBids(decimal Low, decimal High);

internal sealed record EvidenceProduct(Guid Id, string? Title);

internal sealed record EvidenceTrigger(string? Source, string? Summary, EvidenceRegion[]? Regions, DateTimeOffset? ConditionEndsAt);

internal sealed record EvidenceBrief(
    string? Title,
    string? Goal,
    string? Audience,
    string? KeyMessage,
    string? Offer,
    string[]? Regions,
    Guid[]? ProductIds,
    string[]? Channels,
    string? Timing,
    string? Model,
    string? PromptVersion);

internal sealed record RoomEvidence(
    string? Topic,
    string? Reason,
    string[]? Categories,
    EvidenceRegion[]? Regions,
    EvidenceProduct[]? Products,
    EvidenceTrigger? Trigger,
    EvidenceBrief? Brief,
    string[]? Keywords = null,
    long? LastMonthSearches = null,
    double? Growth = null,
    EvidenceBids? Bids = null)
{
    public static RoomEvidence Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RoomEvidence>(json, JsonSerializerOptions.Web) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public static RoomEvidence Empty { get; } = new(null, null, null, null, null, null, null);

    public IReadOnlyList<RoomProduct> RoomProducts() =>
        (Products ?? [])
            .Where(product => product.Id != Guid.Empty && !string.IsNullOrWhiteSpace(product.Title))
            .DistinctBy(product => product.Id)
            .Select(product => new RoomProduct { Id = product.Id.ToString(), Title = product.Title!.Trim() })
            .ToList();

    public IReadOnlyList<string> RegionNames() =>
        RegionsInPlay().Select(region => region.Name!).ToList();

    public IReadOnlyList<EvidenceRegion> RegionsInPlay() =>
        (Trigger?.Regions is { Length: > 0 } triggered ? triggered : Regions ?? [])
            .Where(region => !string.IsNullOrWhiteSpace(region.Name))
            .Select(region => region with { Name = region.Name!.Trim() })
            .DistinctBy(region => region.Name!, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
