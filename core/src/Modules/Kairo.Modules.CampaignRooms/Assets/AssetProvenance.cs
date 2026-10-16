using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed class AssetProvenance
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<AgentCallRecord> AgentCalls { get; set; } = [];

    public List<string> ProductIds { get; set; } = [];

    public List<string> PhotoUrls { get; set; } = [];

    public List<AssetInput> Inputs { get; set; } = [];

    public int? BrandKitVersion { get; set; }

    public int BriefVersion { get; set; }

    public int? PreviousVersion { get; set; }

    public List<string> ChangedFields { get; set; } = [];

    public List<string> MemoryUses { get; set; } = [];

    public static AssetProvenance Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new AssetProvenance() : JsonSerializer.Deserialize<AssetProvenance>(json, Json) ?? new AssetProvenance();

    public string Write() => JsonSerializer.Serialize(this, Json);
}

internal sealed class AgentCallRecord
{
    public string Route { get; set; } = "";

    public string Digest { get; set; } = "";

    public string? Language { get; set; }
}

internal sealed class AssetInput
{
    public Guid AssetId { get; set; }

    public int Version { get; set; }

    public string Kind { get; set; } = "";

    public string? FileKey { get; set; }
}

internal sealed record ProvenanceState(List<AgentCallRecord> Calls, Dictionary<string, List<string>> Photos, List<AssetInput> Inputs);

internal sealed class ProvenanceTrail
{
    public const int MostPhotos = 12;
    public const int MostInputs = 12;

    private static readonly JsonSerializerOptions Requests = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private readonly List<AgentCallRecord> _calls = [];
    private readonly Dictionary<string, List<string>> _photos = new(StringComparer.Ordinal);
    private readonly List<AssetInput> _inputs = [];
    private int? _brandKitVersion;

    public static string Digest<T>(T request) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Requests)))}";

    public void Called<T>(string route, T request, string? language = null)
    {
        var call = new AgentCallRecord { Route = route, Digest = Digest(request), Language = language };
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    public void Photos(Guid productId, IEnumerable<string> urls)
    {
        lock (_gate)
        {
            var list = _photos.TryGetValue(productId.ToString(), out var known) ? known : _photos[productId.ToString()] = [];
            list.AddRange(urls.Where(url => !list.Contains(url, StringComparer.Ordinal)));
        }
    }

    public void Used(Asset asset, int version, string? fileKey)
    {
        lock (_gate)
        {
            if (!_inputs.Any(input => input.AssetId == asset.Id && input.Version == version))
            {
                _inputs.Add(new AssetInput { AssetId = asset.Id, Version = version, Kind = asset.Kind, FileKey = fileKey });
            }
        }
    }

    public void BrandKit(int? version)
    {
        lock (_gate)
        {
            _brandKitVersion = version ?? _brandKitVersion;
        }
    }

    public ProvenanceState Take()
    {
        lock (_gate)
        {
            var state = new ProvenanceState(
                [.. _calls],
                _photos.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.Ordinal),
                [.. _inputs]);
            _calls.Clear();
            _photos.Clear();
            _inputs.Clear();
            return state;
        }
    }

    public void Restore(ProvenanceState state)
    {
        lock (_gate)
        {
            _calls.Clear();
            _calls.AddRange(state.Calls);
            _photos.Clear();
            foreach (var (product, urls) in state.Photos)
            {
                _photos[product] = [.. urls];
            }

            _inputs.Clear();
            _inputs.AddRange(state.Inputs);
        }
    }

    public AssetProvenance For(Asset asset, int? previousVersion, IReadOnlyList<string>? changedFields)
    {
        lock (_gate)
        {
            return Snapshot(asset, previousVersion, changedFields);
        }
    }

    private AssetProvenance Snapshot(Asset asset, int? previousVersion, IReadOnlyList<string>? changedFields)
    {
        var media = !AssetKinds.IsText(asset.Kind);
        return new AssetProvenance
        {
            AgentCalls =
            [
                .. _calls
                    .Where(call => call.Language is null || call.Language == asset.Language)
                    .Select(call => new AgentCallRecord { Route = call.Route, Digest = call.Digest, Language = call.Language }),
            ],
            ProductIds = [.. asset.ProductIds],
            PhotoUrls = media
                ? [.. asset.ProductIds.SelectMany(id => _photos.TryGetValue(id, out var urls) ? urls : []).Distinct(StringComparer.Ordinal).Take(MostPhotos)]
                : [],
            Inputs = [.. _inputs.Where(input => input.AssetId != asset.Id && InputKinds(asset.Kind).Contains(input.Kind)).Take(MostInputs)],
            BrandKitVersion = _brandKitVersion,
            BriefVersion = asset.BriefVersion,
            PreviousVersion = previousVersion is > 0 ? previousVersion : null,
            ChangedFields = [.. changedFields ?? []],
        };
    }

    private static string[] InputKinds(string kind) => kind switch
    {
        AssetKinds.Footage => [AssetKinds.Image],
        AssetKinds.Video or AssetKinds.Voiceover => [AssetKinds.Footage],
        _ => [],
    };
}
