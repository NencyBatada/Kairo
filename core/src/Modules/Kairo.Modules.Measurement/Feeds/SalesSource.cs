namespace Kairo.Modules.Measurement.Feeds;

internal sealed class SalesSource : IBrandOwned
{
    public Guid BrandId { get; set; }

    public string TableRef { get; set; } = "";

    public string DateColumn { get; set; } = "";

    public string DateColumnType { get; set; } = "DATE";

    public string RegionColumn { get; set; } = "";

    public string OrdersColumn { get; set; } = "";

    public string RevenueColumn { get; set; } = "";

    public string Location { get; set; } = "";

    public Guid SetBy { get; set; }

    public DateTimeOffset SetAt { get; set; }
}
