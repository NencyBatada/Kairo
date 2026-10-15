using Kairo.Contracts.Audit;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Format;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record UploadSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

internal static class ImportEndpoints
{
    public const long MaxRequestBytes = CatalogueFile.MaxBytes + 64 * 1024;
    public const int Listed = 50;
    public const string AuditTarget = "catalogue_import";
    public const string ChooseFile = "Choose a product file to upload.";
    public const string TooLarge = "The file is larger than 50 MB.";
    public const string Busy = "Kairo is already importing this catalogue. Try again in a minute.";

    public static async Task<Results<Created<ImportView>, ValidationProblem, ProblemHttpResult>> ImportFileAsync(
        IFormFile? file,
        [FromForm] bool? allowLargeRemoval,
        CatalogueDbContext db,
        CatalogueImporter importer,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileProblem(ChooseFile);
        }

        if (file.Length > CatalogueFile.MaxBytes)
        {
            return FileProblem(TooLarge);
        }

        if (!await importer.TryLockAsync(cancellationToken))
        {
            return BusyProblem();
        }

        byte[] body;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            body = buffer.ToArray();
        }

        var import = await importer.ImportAsync(
            new ImportInput(SourceKinds.File, CleanName(file.FileName), null, member.UserId, allowLargeRemoval == true),
            body,
            cancellationToken);
        await UseFileAsync(db, member, time, cancellationToken);

        var view = ImportView.Of(import);
        await audit.RecordAsync(new AuditRecord("catalogue.file_imported", AuditTarget, import.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/catalogue/imports/{import.Id}", view);
    }

    public static async Task<Ok<ImportView[]>> ListAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        var imports = await db.BrandImports.AsNoTracking()
            .OrderByDescending(import => import.StartedAt)
            .ThenByDescending(import => import.Number)
            .Take(Listed)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(imports.Select(ImportView.Of).ToArray());
    }

    public static async Task<Results<Ok<ImportDetailView>, NotFound>> GetAsync(Guid importId, CatalogueDbContext db, CancellationToken cancellationToken)
    {
        if (await db.BrandImports.AsNoTracking().SingleOrDefaultAsync(import => import.Id == importId, cancellationToken) is not { } import)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ImportDetailView.Of(import));
    }

    public static ProblemHttpResult BusyProblem() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Busy);

    private static async Task UseFileAsync(CatalogueDbContext db, CurrentMember member, TimeProvider time, CancellationToken cancellationToken)
    {
        var source = await db.BrandSources.SingleOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            source = new CatalogueSource { BrandId = member.BrandId };
            db.Sources.Add(source);
        }

        source.Kind = SourceKinds.File;
        source.FeedUrl = null;
        source.SetBy = member.UserId;
        source.SetAt = time.GetUtcNow();
        source.LastCheckedAt = null;
        source.LastSucceededAt = null;
        source.FailuresInRow = 0;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CleanName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0)
        {
            return "catalogue";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    private static ValidationProblem FileProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
