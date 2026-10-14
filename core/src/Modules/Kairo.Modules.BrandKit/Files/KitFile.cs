namespace Kairo.Modules.BrandKit.Files;

internal enum KitFileKind
{
    Guideline,
    Logo,
}

internal sealed class KitFile : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public KitFileKind Kind { get; set; }

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Size { get; set; }

    public string StorageKey { get; set; } = "";

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
