using Kairo.Contracts.BrandKit;
using Kairo.Modules.BrandKit.Data;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class BrandKitReader(BrandKitDbContext db) : IBrandKit
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
}
