using Kairo.Contracts.Live;

namespace Kairo.Modules.BrandKit.Kit;

internal static class BrandKitLiveEvents
{
    public static LiveEvent Changed(KitVersion version) =>
        new("brand_kit.changed", new { number = version.Number, status = KitJson.Name(version.Status) }, LiveAudience.Everyone);

    public static LiveEvent FilesChanged(Guid fileId) =>
        new("brand_kit.files_changed", new { fileId }, LiveAudience.Everyone);
}
