using Kairo.Contracts.BrandKit;
using Kairo.Modules.BrandKit.Data;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class BrandKitReader(BrandKitDbContext db, IFileStore store) : IBrandKit
{
    public async Task<ApprovedBrandKit?> GetApprovedAsync(CancellationToken cancellationToken)
    {
        var approved = await db.Versions
            .AsNoTracking()
            .SingleOrDefaultAsync(version => version.Status == VersionStatus.Approved, cancellationToken);
        if (approved is null)
        {
            return null;
        }

        var ids = KitJson.Read(approved.Content).FileIds();
        var files = await db.Files.AsNoTracking().Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);
        return new ApprovedBrandKit(
            approved.Number,
            KitJson.Element(approved.Content),
            files
                .OrderBy(file => ids.IndexOf(file.Id))
                .Select(file => new BrandKitFile(file.Id, KitJson.Name(file.Kind), file.FileName, file.ContentType))
                .ToList());
    }

    public async Task<BrandKitFileContent?> OpenFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (await db.Files.AsNoTracking().SingleOrDefaultAsync(file => file.Id == fileId, cancellationToken) is not { } file)
        {
            return null;
        }

        try
        {
            await using var stream = await store.OpenAsync(FileBuckets.BrandFiles, file.StorageKey, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return new BrandKitFileContent(file.Id, KitJson.Name(file.Kind), file.ContentType, buffer.ToArray());
        }
        catch (StoredFileMissingException)
        {
            return null;
        }
    }
}
