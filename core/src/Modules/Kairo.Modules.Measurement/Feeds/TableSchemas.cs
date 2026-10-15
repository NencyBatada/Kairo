namespace Kairo.Modules.Measurement.Feeds;

internal sealed record TableColumn(string Name, string Type);

internal sealed record TableSchema(string Name, IReadOnlyList<TableColumn> Columns)
{
    public object SchemaJson() => new
    {
        fields = Columns.Select(column => new { name = column.Name, type = column.Type, mode = column.Name == "date" ? "REQUIRED" : "NULLABLE" }).ToArray(),
    };
}

internal static class TableSchemas
{
    private static readonly TableColumn[] Metrics =
    [
        new("impressions", "INT64"),
        new("clicks", "INT64"),
        new("cost", "NUMERIC"),
        new("conversions", "FLOAT64"),
        new("conversions_value", "NUMERIC"),
    ];

    public static readonly TableSchema AdsCampaignDaily = Of(
        "ads_campaign_daily",
        [Text("customer_id"), Text("campaign_id"), Text("campaign_name"), Text("channel"), Text("campaign_status"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsAdGroupDaily = Of(
        "ads_ad_group_daily",
        [Text("customer_id"), Text("campaign_id"), Text("ad_group_id"), Text("ad_group_name"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsAdDaily = Of(
        "ads_ad_daily",
        [Text("customer_id"), Text("campaign_id"), Text("ad_group_id"), Text("ad_id"), Text("ad_type"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsRegionDaily = Of(
        "ads_region_daily",
        [Text("customer_id"), Text("campaign_id"), Text("region_id"), Text("region_name"), Text("country_code"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsCityDaily = Of(
        "ads_city_daily",
        [Text("customer_id"), Text("campaign_id"), Text("city_id"), Text("city_name"), Text("state_name"), Text("country_code"), Text("currency"), .. Metrics]);

    public static readonly TableSchema Ga4RegionDaily = Of(
        "ga4_region_daily",
        [
            Text("property_id"), Text("country"), Text("region"), Text("currency"),
            new("sessions", "INT64"), new("key_events", "FLOAT64"), new("transactions", "INT64"), new("purchase_revenue", "NUMERIC"),
        ]);

    public static readonly TableSchema Ga4CityDaily = Of(
        "ga4_city_daily",
        [
            Text("property_id"), Text("country"), Text("region"), Text("city"), Text("currency"),
            new("sessions", "INT64"), new("key_events", "FLOAT64"), new("transactions", "INT64"), new("purchase_revenue", "NUMERIC"),
        ]);

    public static readonly TableSchema SalesDaily = Of(
        "sales_daily",
        [Text("region"), new("orders", "INT64"), new("revenue", "NUMERIC"), Text("source")]);

    public static readonly IReadOnlySet<string> AddedLater = new HashSet<string>(StringComparer.Ordinal) { AdsCityDaily.Name, Ga4CityDaily.Name };

    private static TableColumn Text(string name) => new(name, "STRING");

    private static TableSchema Of(string name, TableColumn[] columns) =>
        new(name, [new("date", "DATE"), .. columns, new("loaded_at", "TIMESTAMP")]);
}
