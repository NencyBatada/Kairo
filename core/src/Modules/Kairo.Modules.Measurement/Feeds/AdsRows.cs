using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record AdsAccount(string CustomerId, string TimeZone, string Currency);

internal sealed record GeoTarget(string Id, string? Name, string? CountryCode, string? CanonicalName = null)
{
    public string? StateName => CanonicalName?.Split(',', StringSplitOptions.TrimEntries) is { Length: >= 3 } parts ? parts[^2] : null;
}

internal static class AdsRows
{
    public static JsonObject Campaign(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["campaign_name"] = Text(row, "campaign", "name"),
            ["channel"] = Text(row, "campaign", "advertisingChannelType"),
            ["campaign_status"] = Text(row, "campaign", "status"),
            ["currency"] = account.Currency,
        },
        row);

    public static JsonObject AdGroup(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["ad_group_id"] = Text(row, "adGroup", "id"),
            ["ad_group_name"] = Text(row, "adGroup", "name"),
            ["currency"] = account.Currency,
        },
        row);

    public static JsonObject Ad(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["ad_group_id"] = Text(row, "adGroup", "id"),
            ["ad_id"] = Text(row, "adGroupAd", "ad", "id"),
            ["ad_type"] = Text(row, "adGroupAd", "ad", "type"),
            ["currency"] = account.Currency,
        },
        row);

    public static string? RegionOf(JsonElement row) => Text(row, "segments", GoogleAdsReports.RegionJson);

    public static string? CityOf(JsonElement row) => Text(row, "segments", GoogleAdsReports.CityJson);

    public static JsonObject Region(JsonElement row, AdsAccount account, IReadOnlyDictionary<string, GeoTarget> names)
    {
        var geo = Geo(RegionOf(row), names);
        return WithMetrics(
            new JsonObject
            {
                ["date"] = Text(row, "segments", "date"),
                ["customer_id"] = account.CustomerId,
                ["campaign_id"] = Text(row, "campaign", "id"),
                ["region_id"] = geo?.Id,
                ["region_name"] = geo?.Name,
                ["country_code"] = geo?.CountryCode,
                ["currency"] = account.Currency,
            },
            row);
    }

    public static JsonObject City(JsonElement row, AdsAccount account, IReadOnlyDictionary<string, GeoTarget> names)
    {
        var geo = Geo(CityOf(row), names);
        return WithMetrics(
            new JsonObject
            {
                ["date"] = Text(row, "segments", "date"),
                ["customer_id"] = account.CustomerId,
                ["campaign_id"] = Text(row, "campaign", "id"),
                ["city_id"] = geo?.Id,
                ["city_name"] = geo?.Name,
                ["state_name"] = geo?.StateName,
                ["country_code"] = geo?.CountryCode,
                ["currency"] = account.Currency,
            },
            row);
    }

    public static string? Text(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };
    }

    public static string Money(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static GeoTarget? Geo(string? resource, IReadOnlyDictionary<string, GeoTarget> names) =>
        resource is null
            ? null
            : names.TryGetValue(resource, out var found) ? found : new GeoTarget(resource[(resource.LastIndexOf('/') + 1)..], null, null);

    private static JsonObject WithMetrics(JsonObject row, JsonElement source)
    {
        row["impressions"] = Whole(source, "impressions");
        row["clicks"] = Whole(source, "clicks");
        row["cost"] = Money(Whole(source, "costMicros") / 1_000_000m);
        row["conversions"] = Number(source, "conversions");
        row["conversions_value"] = Money((decimal)Number(source, "conversionsValue"));
        return row;
    }

    private static long Whole(JsonElement row, string metric) =>
        Text(row, "metrics", metric) is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : 0;

    private static double Number(JsonElement row, string metric) =>
        Text(row, "metrics", metric) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : 0;
}
