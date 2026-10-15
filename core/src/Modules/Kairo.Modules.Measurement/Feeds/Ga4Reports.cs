using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record Ga4Property(string Id, string TimeZone, string Currency);

internal static class Ga4Rows
{
    public static JsonObject Of(JsonElement row, Ga4Property property, bool cities)
    {
        var dimensions = Values(row, "dimensionValues");
        var metrics = Values(row, "metricValues");
        var result = new JsonObject
        {
            ["date"] = FeedClock.Iso(DateOnly.ParseExact(dimensions[0] ?? "", "yyyyMMdd", CultureInfo.InvariantCulture)),
            ["property_id"] = property.Id,
            ["country"] = dimensions[1],
            ["region"] = dimensions[2],
        };
        if (cities)
        {
            result["city"] = dimensions[3];
        }

        result["currency"] = property.Currency;
        result["sessions"] = Whole(metrics[0]);
        result["key_events"] = double.Parse(metrics[1] ?? "0", CultureInfo.InvariantCulture);
        result["transactions"] = Whole(metrics[2]);
        result["purchase_revenue"] = AdsRows.Money(decimal.Parse(metrics[3] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture));
        return result;
    }

    private static string?[] Values(JsonElement row, string name) =>
        row.GetProperty(name).EnumerateArray().Select(value => AdsRows.Text(value, "value")).ToArray();

    private static long Whole(string? text) => (long)decimal.Parse(text ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
}

internal sealed class Ga4Reports(IHttpClientFactory http)
{
    public const int PageSize = 100_000;

    private const string AdminUrl = "https://analyticsadmin.googleapis.com/v1beta";
    private const string DataUrl = "https://analyticsdata.googleapis.com/v1beta";

    public async Task<Ga4Property> DescribeAsync(ConnectionAccess access, CancellationToken cancellationToken)
    {
        var property = await http.CreateClient(MeasurementHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{AdminUrl}/properties/{Uri.EscapeDataString(access.ExternalId)}", access.AccessToken, null, cancellationToken)
            .OnConnectionAsync();
        return new Ga4Property(
            access.ExternalId,
            AdsRows.Text(property, "timeZone") ?? FeedClock.IndiaTimeZone,
            AdsRows.Text(property, "currencyCode") ?? "");
    }

    public async Task<List<JsonObject>> DailyAsync(ConnectionAccess access, Ga4Property property, FeedWindow window, bool cities, CancellationToken cancellationToken)
    {
        string[] names = cities ? ["date", "country", "region", "city"] : ["date", "country", "region"];
        var client = http.CreateClient(MeasurementHttp.ClientName);
        var rows = new List<JsonObject>();
        var offset = 0;
        while (true)
        {
            var page = await client.CallAsync(
                HttpMethod.Post,
                $"{DataUrl}/properties/{Uri.EscapeDataString(property.Id)}:runReport",
                access.AccessToken,
                new
                {
                    dateRanges = new[] { new { startDate = FeedClock.Iso(window.From), endDate = FeedClock.Iso(window.To) } },
                    dimensions = names.Select(name => new { name }).ToArray(),
                    metrics = new[] { new { name = "sessions" }, new { name = "keyEvents" }, new { name = "transactions" }, new { name = "purchaseRevenue" } },
                    limit = PageSize,
                    offset,
                },
                cancellationToken).OnConnectionAsync();
            var read = 0;
            if (page.TryGetProperty("rows", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in list.EnumerateArray())
                {
                    rows.Add(Ga4Rows.Of(row, property, cities));
                    read++;
                }
            }

            offset += read;
            var total = page.TryGetProperty("rowCount", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;
            if (read == 0 || offset >= total)
            {
                return rows;
            }
        }
    }
}
