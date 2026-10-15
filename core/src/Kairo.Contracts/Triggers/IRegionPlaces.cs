namespace Kairo.Contracts.Triggers;

public interface IRegionPlaces
{
    RegionPlace? Find(string name);
}

public sealed record RegionPlace(string Name, string Kind, string? State, double? Latitude, double? Longitude);
