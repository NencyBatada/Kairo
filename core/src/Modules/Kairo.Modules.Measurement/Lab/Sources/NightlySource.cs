using System.Globalization;
using System.Net;
using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement.Lab.Sources;

internal sealed class NightlySource(
    BigQueryClient bigQuery,
    IServiceAccessToken serviceToken,
    AnalyticsOptions options,
    DataCoverage coverage) : ILabSource
{
    public bool Handles(LabSourceSpec spec) => spec.Kind is LabKinds.GoogleAds or LabKinds.Ga4 && !LabGrains.IsLive(spec.Grain);

    public Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken) =>
        coverage.CoveredAsync(spec.Kind == LabKinds.Ga4 ? FeedNames.Ga4 : FeedNames.GoogleAds, cancellationToken);

    public async Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (options.Project is not { } project)
        {
            throw new LabDataProblem(AnalyticsOptions.NotSetUp);
        }

        var token = await serviceToken.GetAsync(cancellationToken);
        var table = $"`{project}.{AnalyticsOptions.DatasetFor(brandId)}.{TableOf(spec).Name}`";
        IReadOnlyList<string?[]> rows;
        try
        {
            rows = await bigQuery.QueryAsync(
                project, token, LabQueries.Nightly(spec, table), options.Location, [QueryParameter.Date("from", from), QueryParameter.Date("to", to)], cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.Status == HttpStatusCode.NotFound)
        {
            rows = [];
        }

        var builder = new LabSeriesBuilder(from, to);
        string? currency = null;
        foreach (var row in rows)
        {
            if (!DateOnly.TryParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || row[2] is not { } name)
            {
                continue;
            }

            var key = LabGrains.IsRegion(spec.Grain) ? RegionKeys.Of(name) : row[1];
            builder.Add(key, name, date, LabQueries.Number(row[3]));
            currency ??= row[4];
        }

        return builder.Build(LabMetrics.IsMoney(spec.Metric) ? currency : null);
    }

    private static TableSchema TableOf(LabSourceSpec spec) => (spec.Kind, spec.Grain) switch
    {
        (LabKinds.GoogleAds, LabGrains.State) => TableSchemas.AdsRegionDaily,
        (LabKinds.GoogleAds, LabGrains.City) => TableSchemas.AdsCityDaily,
        (LabKinds.GoogleAds, LabGrains.Campaign) => TableSchemas.AdsCampaignDaily,
        (LabKinds.Ga4, LabGrains.State) => TableSchemas.Ga4RegionDaily,
        (LabKinds.Ga4, LabGrains.City) => TableSchemas.Ga4CityDaily,
        _ => throw new LabDataProblem(LabSourceRules.BadGrain),
    };
}

internal static class LabQueries
{
    public static string Nightly(LabSourceSpec spec, string table)
    {
        var (key, name) = (spec.Kind, spec.Grain) switch
        {
            (LabKinds.GoogleAds, LabGrains.State) => ("region_name", "region_name"),
            (LabKinds.GoogleAds, LabGrains.City) => ("city_name", "city_name"),
            (LabKinds.GoogleAds, LabGrains.Campaign) => ("campaign_id", "campaign_name"),
            (LabKinds.Ga4, LabGrains.State) => ("region", "region"),
            (LabKinds.Ga4, LabGrains.City) => ("city", "city"),
            _ => throw new LabDataProblem(LabSourceRules.BadGrain),
        };
        var value = spec.Metric switch
        {
            LabMetrics.Conversions => "conversions",
            LabMetrics.ConversionValue => "conversions_value",
            LabMetrics.Clicks => "clicks",
            LabMetrics.Transactions => "transactions",
            LabMetrics.PurchaseRevenue => "purchase_revenue",
            LabMetrics.Sessions => "sessions",
            LabMetrics.KeyEvents => "key_events",
            _ => throw new LabDataProblem(LabSourceRules.BadMetric),
        };
        return $"""
            SELECT CAST(date AS STRING), {key}, ANY_VALUE({name}), SUM({value}), ANY_VALUE(currency)
            FROM {table}
            WHERE date BETWEEN @from AND @to AND {key} IS NOT NULL AND {key} != '(not set)'
            GROUP BY 1, 2
            """;
    }

    public static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Max(value, 0) : 0;
}
