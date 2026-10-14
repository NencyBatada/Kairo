namespace Kairo.Modules.BrandKit.Files;

internal sealed record KitFileView(
    Guid Id,
    KitFileKind Kind,
    string FileName,
    string ContentType,
    long Size,
    bool Deleted,
    Guid UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static KitFileView Of(KitFile file) => new(
        file.Id, file.Kind, file.FileName, file.ContentType, file.Size, file.DeletedAt is not null, file.UploadedBy, file.UploadedAt);
}
