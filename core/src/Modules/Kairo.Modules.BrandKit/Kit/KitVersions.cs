using System.Globalization;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class KitVersions(BrandKitDbContext db, KairoDatabase database, CurrentMember member, TimeProvider time)
{
    public const string AuditTarget = "brand_kit_version";
    public const string StaleDraft = "Someone else changed the draft. Reload it and try again.";
    public const string NoDraft = "There's no draft.";

    public Task<KitVersion?> DraftAsync(CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(version => version.Status == VersionStatus.Draft, cancellationToken);

    public Task<KitVersion?> ApprovedAsync(CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(version => version.Status == VersionStatus.Approved, cancellationToken);

    public Task<KitVersion?> FindAsync(int number, CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(
            version => version.Number == number && version.Status != VersionStatus.Discarded,
            cancellationToken);

    public Task LockAsync(CancellationToken cancellationToken) =>
        database.LockAsync($"brand-kit:{member.BrandId:N}", cancellationToken);

    public async Task<KitVersion> CreateDraftAsync(KitContent content, VersionOrigin origin, int? restoredFrom, CancellationToken cancellationToken)
    {
        var number = (await db.Versions.MaxAsync(version => (int?)version.Number, cancellationToken) ?? 0) + 1;
        var now = time.GetUtcNow();
        var draft = new KitVersion
        {
            Id = Guid.NewGuid(),
            Number = number,
            Status = VersionStatus.Draft,
            Content = KitJson.Write(content),
            Revision = 1,
            Origin = origin,
            RestoredFrom = restoredFrom,
            CreatedBy = member.UserId,
            CreatedAt = now,
            UpdatedBy = member.UserId,
            UpdatedAt = now,
        };
        db.Versions.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return draft;
    }

    public async Task UpdateDraftAsync(KitVersion draft, KitContent content, CancellationToken cancellationToken)
    {
        draft.Content = KitJson.Write(content);
        draft.Revision++;
        draft.UpdatedBy = member.UserId;
        draft.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RestoreIntoAsync(KitVersion draft, KitContent content, int from, CancellationToken cancellationToken)
    {
        draft.Origin = VersionOrigin.Restored;
        draft.RestoredFrom = from;
        return UpdateDraftAsync(draft, content, cancellationToken);
    }

    public async Task ApproveAsync(KitVersion draft, CancellationToken cancellationToken)
    {
        if (await ApprovedAsync(cancellationToken) is { } current)
        {
            current.Status = VersionStatus.Superseded;
            await db.SaveChangesAsync(cancellationToken);
        }

        draft.Status = VersionStatus.Approved;
        draft.ApprovedBy = member.UserId;
        draft.ApprovedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardAsync(KitVersion draft, CancellationToken cancellationToken)
    {
        draft.Status = VersionStatus.Discarded;
        draft.UpdatedBy = member.UserId;
        draft.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<KitVersionView> ViewAsync(KitVersion version, CancellationToken cancellationToken)
    {
        var content = KitJson.Read(version.Content);
        var ids = content.FileIds();
        var files = await db.Files.Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);
        return KitVersionView.Of(
            version,
            content,
            files.OrderBy(file => ids.IndexOf(file.Id)).Select(KitFileView.Of).ToArray());
    }

    public static string Number(KitVersion version) => version.Number.ToString(CultureInfo.InvariantCulture);

    public static object AuditOf(KitVersion version) => new
    {
        number = version.Number,
        status = KitJson.Name(version.Status),
        revision = version.Revision,
        content = KitJson.Element(version.Content),
    };
}
