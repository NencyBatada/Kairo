using Kairo.Contracts.Measurement;
using Kairo.Contracts.Triggers;

namespace Kairo.Modules.Triggers.Regions;

internal sealed class RegionPlaces : IRegionPlaces
{
    private static readonly Dictionary<string, Region> ByName = IndiaRegions.All
        .Where(region => region.Kind != RegionKinds.Country)
        .GroupBy(region => RegionKeys.Of(region.Name))
        .ToDictionary(group => group.Key, group => group.OrderBy(region => region.Kind == RegionKinds.City ? 0 : 1).First(), StringComparer.Ordinal);

    public RegionPlace? Find(string name) =>
        ByName.TryGetValue(RegionKeys.Of(name), out var region)
            ? new RegionPlace(
                region.Name,
                region.Kind,
                region.StateId is { } state ? IndiaRegions.NameOf(state) : null,
                region.Latitude,
                region.Longitude)
            : null;
}
