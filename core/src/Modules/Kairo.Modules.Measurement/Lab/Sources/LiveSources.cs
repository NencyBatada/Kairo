using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Lab.Sources;

internal static class LiveData
{
    public const string NotSet = "(not set)";

    public static DateOnly Yesterday(TimeProvider time) => FeedClock.Today(time, FeedClock.IndiaTimeZone).AddDays(-1);

    public static string? KeyOf(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim() == NotSet ? null : name.Trim().ToLowerInvariant();
}

internal sealed class Ga4ItemSource(IConnections connections, Ga4Reports reports, TimeProvider time) : ILabSource
{
    public bool Handles(LabSourceSpec spec) => spec.Kind == LabKinds.Ga4 && LabGrains.IsLive(spec.Grain);

    public Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken) =>
        Task.FromResult<DateOnly?>(LiveData.Yesterday(time));

    public async Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var access = await connections.GetAccessAsync(ConnectedProduct.Ga4, cancellationToken);
        var property = await reports.DescribeAsync(access, cancellationToken);
        var rows = await reports.RowsAsync(
            access,
            property.Id,
            from,
            to,
            ["date", spec.Grain == LabGrains.Category ? "itemCategory" : "itemName"],
            [spec.Metric == LabMetrics.ItemRevenue ? "itemRevenue" : "itemsPurchased"],
            cancellationToken);
        var builder = new LabSeriesBuilder(from, to);
        foreach (var row in rows)
        {
            var dimensions = Ga4Rows.Values(row, "dimensionValues");
            var metrics = Ga4Rows.Values(row, "metricValues");
            if (DateOnly.TryParseExact(dimensions[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                builder.Add(LiveData.KeyOf(dimensions[1]), dimensions[1], date, LabQueries.Number(metrics[0]));
            }
        }

        return builder.Build(LabMetrics.IsMoney(spec.Metric) ? property.Currency : null);
    }
}

internal sealed class AdsProductSource(IConnections connections, GoogleAdsReports reports, TimeProvider time) : ILabSource
{
    public bool Handles(LabSourceSpec spec) => spec.Kind == LabKinds.GoogleAds && LabGrains.IsLive(spec.Grain);

    public Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken) =>
        Task.FromResult<DateOnly?>(LiveData.Yesterday(time));

    public async Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var access = await connections.GetAccessAsync(ConnectedProduct.GoogleAds, cancellationToken);
        var account = await reports.DescribeAsync(access, cancellationToken);
        var category = spec.Grain == LabGrains.Category;
        var rows = await reports.ProductsAsync(access, new FeedWindow(from, to), category, cancellationToken);
        var builder = new LabSeriesBuilder(from, to);
        foreach (var row in rows)
        {
            if (AdsRows.Text(row, "segments", "date") is not { } day
                || !DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var metrics = row.TryGetProperty("metrics", out var found) ? found : default;
            var value = spec.Metric switch
            {
                LabMetrics.Clicks => Number(metrics, "clicks"),
                LabMetrics.ConversionValue => Number(metrics, "conversionsValue"),
                _ => Number(metrics, "conversions"),
            };
            if (category)
            {
                var name = AdsRows.Text(row, "segments", "productTypeL1");
                builder.Add(LiveData.KeyOf(name), name, date, value);
            }
            else
            {
                var id = AdsRows.Text(row, "segments", "productItemId");
                builder.Add(LiveData.KeyOf(id), AdsRows.Text(row, "segments", "productTitle") ?? id, date, value);
            }
        }

        return builder.Build(LabMetrics.IsMoney(spec.Metric) ? account.Currency : null);
    }

    private static double Number(JsonElement metrics, string name) =>
        metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number ? Math.Max(value.GetDouble(), 0) : LabQueries.Number(value.GetString())
            : 0;
}
