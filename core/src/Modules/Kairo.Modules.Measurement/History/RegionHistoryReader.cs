using System.Globalization;
using System.Net;
using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.History;

internal static class HistoryQueries
{
    public static string AdsCities(string table) => $"""
        SELECT CAST(date AS STRING), city_id, city_name, state_name, SUM(conversions), ANY_VALUE(currency), SUM(cost)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND city_name IS NOT NULL
        GROUP BY date, city_id, city_name, state_name
        """;

    public static string AdsStates(string table) => $"""
        SELECT CAST(date AS STRING), region_id, region_name, CAST(NULL AS STRING), SUM(conversions), ANY_VALUE(currency), SUM(cost)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND region_name IS NOT NULL
        GROUP BY date, region_id, region_name
        """;

    public static string Ga4Cities(string table) => $"""
        SELECT CAST(date AS STRING), city, region, SUM(transactions), SUM(purchase_revenue), ANY_VALUE(currency)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND city IS NOT NULL AND city != '(not set)'
        GROUP BY date, city, region
        """;

    public static string Ga4States(string table) => $"""
        SELECT CAST(date AS STRING), region, CAST(NULL AS STRING), SUM(transactions), SUM(purchase_revenue), ANY_VALUE(currency)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND region IS NOT NULL AND region != '(not set)'
        GROUP BY date, region
        """;

    public static string Sales(string table) => $"""
        SELECT CAST(date AS STRING), region, SUM(orders), SUM(revenue)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND region IS NOT NULL
        GROUP BY date, region
        """;
}

internal sealed class RegionHistoryReader(BigQueryClient bigQuery, IServiceAccessToken serviceToken, AnalyticsOptions options) : IRegionHistory
{
    public async Task<RegionHistory> DailyAsync(Guid brandId, RegionUnit unit, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var history = new Builder(unit, from, to);
        if (options.Project is not { } project || to < from)
        {
            return history.Build();
        }

        var token = await serviceToken.GetAsync(cancellationToken);
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        string Table(TableSchema table) => $"`{project}.{dataset}.{table.Name}`";
        async Task<IReadOnlyList<string?[]>> ReadAsync(string sql)
        {
            try
            {
                return await bigQuery.QueryAsync(
                    project, token, sql, options.Location, [QueryParameter.Date("from", from), QueryParameter.Date("to", to)], cancellationToken);
            }
            catch (GoogleApiException exception) when (exception.Status == HttpStatusCode.NotFound)
            {
                return [];
            }
        }

        var cities = unit == RegionUnit.City;
        foreach (var row in await ReadAsync(cities ? HistoryQueries.AdsCities(Table(TableSchemas.AdsCityDaily)) : HistoryQueries.AdsStates(Table(TableSchemas.AdsRegionDaily))))
        {
            history.Ads(row[0], row[1], row[2], row[3], Number(row[4]), row[5], Number(row[6]));
        }

        foreach (var row in await ReadAsync(cities ? HistoryQueries.Ga4Cities(Table(TableSchemas.Ga4CityDaily)) : HistoryQueries.Ga4States(Table(TableSchemas.Ga4RegionDaily))))
        {
            history.Ga4(row[0], row[1], row[2], Number(row[3]), Number(row[4]), row[5]);
        }

        foreach (var row in await ReadAsync(HistoryQueries.Sales(Table(TableSchemas.SalesDaily))))
        {
            history.Sale(row[0], row[1], Number(row[2]), Number(row[3]));
        }

        return history.Build();
    }

    private static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Max(value, 0) : 0;

    private sealed class Builder(RegionUnit unit, DateOnly from, DateOnly to)
    {
        private readonly Dictionary<string, Place> _places = new(StringComparer.Ordinal);
        private readonly HashSet<string> _states = new(StringComparer.Ordinal);
        private readonly List<(string Key, string Name, int Day, double Orders, double Revenue)> _sales = [];
        private string? _currency;

        private int Days => to.DayNumber - from.DayNumber + 1;

        public void Ads(string? date, string? geoId, string? name, string? state, double conversions, string? currency, double cost)
        {
            if (Index(date) is not { } day || Clean(name) is not { } clean)
            {
                return;
            }

            var place = PlaceOf(clean, state);
            place.GeoTargetId ??= Clean(geoId);
            (place.AdsConversions ??= new double[Days])[day] += conversions;
            (place.Cost ??= new double[Days])[day] += cost;
            _currency ??= Clean(currency);
        }

        public void Ga4(string? date, string? name, string? state, double transactions, double revenue, string? currency)
        {
            if (Index(date) is not { } day || Clean(name) is not { } clean)
            {
                return;
            }

            var place = PlaceOf(clean, state);
            (place.Transactions ??= new double[Days])[day] += transactions;
            (place.Ga4Revenue ??= new double[Days])[day] += revenue;
            _currency = Clean(currency) ?? _currency;
        }

        public void Sale(string? date, string? name, double orders, double revenue)
        {
            if (Index(date) is { } day && Clean(name) is { } clean)
            {
                _sales.Add((RegionKeys.Of(clean), clean, day, orders, revenue));
            }
        }

        public RegionHistory Build()
        {
            foreach (var sale in _sales)
            {
                if (sale.Key.Length == 0 || (unit == RegionUnit.City && !_places.ContainsKey(sale.Key) && _states.Contains(sale.Key)))
                {
                    continue;
                }

                var place = PlaceOf(sale.Name, null);
                (place.Orders ??= new double[Days])[sale.Day] += sale.Orders;
                (place.Revenue ??= new double[Days])[sale.Day] += sale.Revenue;
            }

            var regions = _places.Values
                .OrderBy(place => place.Key, StringComparer.Ordinal)
                .Select(place => new RegionSeries(
                    place.Key,
                    place.Name,
                    place.GeoTargetId,
                    place.State,
                    place.Orders,
                    place.Transactions ?? place.AdsConversions,
                    place.Revenue ?? place.Ga4Revenue,
                    place.Cost))
                .ToList();
            return new RegionHistory(unit, from, to, _sales.Count > 0, _currency, regions);
        }

        private Place PlaceOf(string name, string? state)
        {
            var key = RegionKeys.Of(name);
            if (!_places.TryGetValue(key, out var place))
            {
                place = new Place(key, name);
                _places[key] = place;
            }

            if (Clean(state) is { } known)
            {
                place.State ??= known;
                _states.Add(RegionKeys.Of(known));
            }

            return place;
        }

        private int? Index(string? date)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day < from || day > to)
            {
                return null;
            }

            return day.DayNumber - from.DayNumber;
        }

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed class Place(string key, string name)
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        public string? GeoTargetId { get; set; }

        public string? State { get; set; }

        public double[]? Orders { get; set; }

        public double[]? Revenue { get; set; }

        public double[]? Transactions { get; set; }

        public double[]? Ga4Revenue { get; set; }

        public double[]? AdsConversions { get; set; }

        public double[]? Cost { get; set; }
    }
}
