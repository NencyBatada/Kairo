using Kairo.Contracts.Live;
using Kairo.Modules.BrandKit.Drafting;

namespace Kairo.Modules.BrandKit.Kit;

internal static class BrandKitLiveEvents
{
    public static LiveEvent Changed(KitVersion version) =>
        new("brand_kit.changed", new { number = version.Number, status = KitJson.Name(version.Status) }, LiveAudience.Everyone);

    public static LiveEvent FilesChanged(Guid fileId) =>
        new("brand_kit.files_changed", new { fileId }, LiveAudience.Everyone);

    public static LiveEvent Drafting(Guid runId, DraftRunStatus status) =>
        new("brand_kit.drafting", new { runId, status = KitJson.Name(status) }, LiveAudience.Roles(BrandRoles.Admin));
}
