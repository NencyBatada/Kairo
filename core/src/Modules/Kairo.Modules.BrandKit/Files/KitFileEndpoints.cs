using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Kit;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Kairo.Modules.BrandKit.Files;

internal sealed record UploadSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

internal static class KitFileEndpoints
{
    public const long MaxRequestBytes = KitFileTypes.MaxBytes + 64 * 1024;
    public const string AuditTarget = "brand_kit_file";
    public const string ChooseFile = "Choose a file to upload.";
    public const string TooLarge = "The file is larger than 20 MB.";
    public const string WrongType = "Upload a PDF, PNG, JPEG, WebP, SVG or Word (.docx) file.";
    public const string NotAnImage = "A logo must be a PNG, JPEG, WebP or SVG image.";
    public const string BadKind = "Use guideline or logo.";

    public static async Task<Results<Created<KitFileView>, ValidationProblem>> UploadAsync(
        IFormFile? file,
        [FromForm] string? kind,
        BrandKitDbContext db,
        IFileStore store,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (KitFileTypes.ParseKind(kind) is not { } fileKind)
        {
            return Problem("kind", BadKind);
        }

        if (file is null)
        {
            return Problem("file", ChooseFile);
        }

        if (file.Length > KitFileTypes.MaxBytes)
        {
            return Problem("file", TooLarge);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (KitFileTypes.Detect(bytes, file.FileName) is not { } contentType)
        {
            return Problem("file", WrongType);
        }

        if (fileKind == KitFileKind.Logo && !KitFileTypes.IsImage(contentType))
        {
            return Problem("file", NotAnImage);
        }

        var id = Guid.NewGuid();
        var row = new KitFile
        {
            Id = id,
            Kind = fileKind,
            FileName = KitFileTypes.CleanName(file.FileName),
            ContentType = contentType,
            Size = bytes.LongLength,
            StorageKey = FileKeys.For(member.BrandId, fileKind == KitFileKind.Logo ? "logos" : "guidelines", id),
            UploadedBy = member.UserId,
            UploadedAt = time.GetUtcNow(),
        };
        await store.PutAsync(FileBuckets.BrandFiles, row.StorageKey, bytes, contentType, cancellationToken);
        db.Files.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var view = KitFileView.Of(row);
        await audit.RecordAsync(new AuditRecord("brand_kit.file_uploaded", AuditTarget, id.ToString(), After: view), cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.FilesChanged(id), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/brand-kit/files/{id}", view);
    }

    public static async Task<Ok<KitFileView[]>> ListAsync(BrandKitDbContext db, CancellationToken cancellationToken)
    {
        var files = await db.Files
            .Where(file => file.DeletedAt == null)
            .OrderByDescending(file => file.UploadedAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(files.Select(KitFileView.Of).ToArray());
    }

    public static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid fileId,
        BrandKitDbContext db,
        IFileStore store,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await db.Files.AsNoTracking().SingleOrDefaultAsync(file => file.Id == fileId, cancellationToken) is not { } file)
        {
            return TypedResults.NotFound();
        }

        var stream = await store.OpenAsync(FileBuckets.BrandFiles, file.StorageKey, cancellationToken);
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        return TypedResults.Stream(stream, file.ContentType, file.FileName);
    }

    public static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid fileId,
        BrandKitDbContext db,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await db.Files.SingleOrDefaultAsync(file => file.Id == fileId && file.DeletedAt == null, cancellationToken) is not { } file)
        {
            return TypedResults.NotFound();
        }

        var before = KitFileView.Of(file);
        file.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        if (await versions.DraftAsync(cancellationToken) is { } draft)
        {
            var content = KitJson.Read(draft.Content);
            if (content.FileIds().Contains(fileId))
            {
                await versions.UpdateDraftAsync(draft, content.WithoutFile(fileId), cancellationToken);
                await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
            }
        }

        await audit.RecordAsync(new AuditRecord("brand_kit.file_deleted", AuditTarget, fileId.ToString(), before), cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.FilesChanged(fileId), cancellationToken);
        return TypedResults.NoContent();
    }

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
