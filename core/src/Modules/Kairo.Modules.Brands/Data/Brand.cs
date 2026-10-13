namespace Kairo.Modules.Brands.Data;

internal sealed class Brand
{
    public Guid Id { get; set; }

    public string Handle { get; set; } = "";

    public string Name { get; set; } = "";

    public string TimeZone { get; set; } = "";

    public string Currency { get; set; } = "";

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
