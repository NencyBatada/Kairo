namespace Kairo.Modules.Measurement.Feeds;

internal sealed class SalesUpload : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string FileName { get; set; } = "";

    public int Rows { get; set; }

    public DateOnly FirstDate { get; set; }

    public DateOnly LastDate { get; set; }

    public int Days { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
