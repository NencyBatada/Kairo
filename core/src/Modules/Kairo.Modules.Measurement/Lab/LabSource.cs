using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Lab;

internal sealed record LabSourceSpec(
    string Kind,
    string Metric,
    string Grain,
    string? Table = null,
    string? DateColumn = null,
    string? ValueColumn = null,
    string? SeriesColumn = null)
{
    public string Write() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static LabSourceSpec Read(string json) => JsonSerializer.Deserialize<LabSourceSpec>(json, JsonSerializerOptions.Web)!;
}

internal sealed record LabSourceRequest(
    string? Kind,
    string? Metric,
    string? Grain,
    string? Table,
    string? DateColumn,
    string? ValueColumn,
    string? SeriesColumn);

internal sealed record LabSeriesData(string Key, string Name, double[] Values);

internal sealed record LabData(DateOnly From, DateOnly To, string? Currency, IReadOnlyList<LabSeriesData> Series)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

internal interface ILabSource
{
    bool Handles(LabSourceSpec spec);

    Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken);

    Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

internal sealed class LabDataProblem(string message) : Exception(message);

internal static class LabKinds
{
    public const string GoogleAds = "google_ads";
    public const string Ga4 = "ga4";
    public const string BigQuery = "bigquery";
    public const string File = "file";

    public static readonly string[] All = [GoogleAds, Ga4, BigQuery, File];

    public static ConnectedProduct? Product(string kind) => kind switch
    {
        GoogleAds => ConnectedProduct.GoogleAds,
        Ga4 => ConnectedProduct.Ga4,
        BigQuery => ConnectedProduct.BigQuery,
        _ => null,
    };
}

internal static class LabGrains
{
    public const string State = "state";
    public const string City = "city";
    public const string Campaign = "campaign";
    public const string Category = "category";
    public const string Product = "product";
    public const string Series = "series";

    public static bool IsRegion(string grain) => grain is State or City;

    public static bool IsLive(string grain) => grain is Category or Product;
}

internal static class LabMetrics
{
    public const string Conversions = "conversions";
    public const string ConversionValue = "conversion_value";
    public const string Clicks = "clicks";
    public const string Transactions = "transactions";
    public const string PurchaseRevenue = "purchase_revenue";
    public const string Sessions = "sessions";
    public const string KeyEvents = "key_events";
    public const string ItemsPurchased = "items_purchased";
    public const string ItemRevenue = "item_revenue";
    public const int LabelLength = 60;

    private static readonly string[] AdsMetrics = [Conversions, ConversionValue, Clicks];
    private static readonly string[] Ga4SiteMetrics = [Transactions, PurchaseRevenue, Sessions, KeyEvents];
    private static readonly string[] Ga4ItemMetrics = [ItemsPurchased, ItemRevenue];

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> Catalog =
        new Dictionary<string, IReadOnlyDictionary<string, string[]>>
        {
            [LabKinds.GoogleAds] = new Dictionary<string, string[]>
            {
                [LabGrains.State] = AdsMetrics,
                [LabGrains.City] = AdsMetrics,
                [LabGrains.Campaign] = AdsMetrics,
                [LabGrains.Category] = AdsMetrics,
                [LabGrains.Product] = AdsMetrics,
            },
            [LabKinds.Ga4] = new Dictionary<string, string[]>
            {
                [LabGrains.State] = Ga4SiteMetrics,
                [LabGrains.City] = Ga4SiteMetrics,
                [LabGrains.Category] = Ga4ItemMetrics,
                [LabGrains.Product] = Ga4ItemMetrics,
            },
        };

    public static bool IsMoney(string metric) => metric is ConversionValue or PurchaseRevenue or ItemRevenue;

    public static string Label(LabSourceSpec spec) => spec.Metric.Replace('_', ' ');
}

internal static class LabSourceRules
{
    public const string BadKind = "Use google_ads, ga4, bigquery or file.";
    public const string BadGrain = "This source can't split the data that way.";
    public const string BadMetric = "This source doesn't offer that outcome.";
    public const string NeedsLabel = "Name the outcome in 60 characters or fewer.";

    public static (LabSourceSpec? Spec, Dictionary<string, string[]> Errors) Check(LabSourceRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        var kind = request?.Kind?.Trim();
        if (kind is null || !LabKinds.All.Contains(kind))
        {
            errors["source.kind"] = [BadKind];
            return (null, errors);
        }

        if (kind is LabKinds.BigQuery or LabKinds.File)
        {
            var label = request!.Metric?.Trim();
            if (label is not { Length: > 0 and <= LabMetrics.LabelLength })
            {
                errors["source.metric"] = [NeedsLabel];
            }

            if (kind == LabKinds.File)
            {
                return errors.Count > 0 ? (null, errors) : (new LabSourceSpec(kind, label!, LabGrains.Series), errors);
            }

            var table = TableRef.Parse(request.Table?.Trim());
            if (table is null)
            {
                errors["source.table"] = [SalesEndpoints.BadTable];
            }

            var columns = new Dictionary<string, string?>
            {
                ["dateColumn"] = request.DateColumn?.Trim(),
                ["valueColumn"] = request.ValueColumn?.Trim(),
                ["seriesColumn"] = request.SeriesColumn?.Trim(),
            };
            foreach (var (role, column) in columns)
            {
                if (!SalesColumnRules.IsSafeName(column))
                {
                    errors[$"source.{role}"] = [SalesEndpoints.BadColumn];
                }
            }

            return errors.Count > 0
                ? (null, errors)
                : (new LabSourceSpec(kind, label!, LabGrains.Series, table!.ToString(), columns["dateColumn"], columns["valueColumn"], columns["seriesColumn"]), errors);
        }

        var grains = LabMetrics.Catalog[kind];
        var grain = request!.Grain?.Trim();
        if (grain is null || !grains.TryGetValue(grain, out var metrics))
        {
            errors["source.grain"] = [BadGrain];
            return (null, errors);
        }

        var metric = request.Metric?.Trim();
        if (metric is null || !metrics.Contains(metric))
        {
            errors["source.metric"] = [BadMetric];
            return (null, errors);
        }

        return (new LabSourceSpec(kind, metric, grain), errors);
    }
}

internal sealed class LabSeriesBuilder(DateOnly from, DateOnly to)
{
    public const int MostSeries = 200;
    public const int KeyLength = 200;

    private readonly Dictionary<string, (string Name, double[] Values)> _series = new(StringComparer.Ordinal);

    public void Add(string? key, string? name, DateOnly date, double value)
    {
        var cleanKey = Cut(key);
        var cleanName = Cut(name) ?? cleanKey;
        if (cleanKey is null || cleanName is null || date < from || date > to)
        {
            return;
        }

        if (!_series.TryGetValue(cleanKey, out var series))
        {
            series = (cleanName, new double[to.DayNumber - from.DayNumber + 1]);
            _series[cleanKey] = series;
        }

        series.Values[date.DayNumber - from.DayNumber] += double.IsFinite(value) && value > 0 ? value : 0;
    }

    public LabData Build(string? currency)
    {
        var kept = _series
            .Select(entry => (Key: entry.Key, entry.Value.Name, entry.Value.Values, Total: entry.Value.Values.Sum()))
            .Where(entry => entry.Total > 0)
            .OrderByDescending(entry => entry.Total)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(MostSeries)
            .ToList();
        if (kept.Count == 0)
        {
            throw new LabDataProblem("The source has no data for this window.");
        }

        var first = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).First(day => kept.Any(entry => entry.Values[day] > 0));
        return new LabData(
            from.AddDays(first),
            to,
            currency is { Length: 3 } ? currency : null,
            [.. kept.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Select(entry => new LabSeriesData(entry.Key, entry.Name, entry.Values[first..]))]);
    }

    public static string? Cut(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length == 0 ? null : trimmed.Length > KeyLength ? trimmed[..KeyLength] : trimmed;
    }
}
