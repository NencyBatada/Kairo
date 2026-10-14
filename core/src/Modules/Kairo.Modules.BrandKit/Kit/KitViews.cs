using Kairo.Modules.BrandKit.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed record SaveDraftRequest(KitContent? Content, int Revision);

internal sealed record RevisionRequest(int Revision);

internal sealed record KitVersionView(
    int Number,
    VersionStatus Status,
    VersionOrigin Origin,
    int? RestoredFrom,
    int Revision,
    KitContent Content,
    KitFileView[] Files,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt)
{
    public static KitVersionView Of(KitVersion version, KitContent content, KitFileView[] files) => new(
        version.Number,
        version.Status,
        version.Origin,
        version.RestoredFrom,
        version.Revision,
        content,
        files,
        version.CreatedBy,
        version.CreatedAt,
        version.UpdatedBy,
        version.UpdatedAt,
        version.ApprovedBy,
        version.ApprovedAt);
}

internal sealed record BrandKitView(KitVersionView? Approved, KitVersionView? Draft);

internal sealed record KitVersionSummary(
    int Number,
    VersionStatus Status,
    VersionOrigin Origin,
    int? RestoredFrom,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt)
{
    public static KitVersionSummary Of(KitVersion version) => new(
        version.Number,
        version.Status,
        version.Origin,
        version.RestoredFrom,
        version.CreatedBy,
        version.CreatedAt,
        version.UpdatedBy,
        version.UpdatedAt,
        version.ApprovedBy,
        version.ApprovedAt);
}
