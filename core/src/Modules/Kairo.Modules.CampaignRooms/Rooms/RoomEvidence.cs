using System.Text.Json;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed record EvidenceRegion(string? Id, string? Name);

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
    EvidenceBrief? Brief)
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
        (Trigger?.Regions is { Length: > 0 } triggered ? triggered : Regions ?? [])
            .Select(region => region.Name?.Trim())
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
