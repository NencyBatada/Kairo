using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record SalesUploadView(
    Guid Id,
    string FileName,
    int Rows,
    DateOnly FirstDate,
    DateOnly LastDate,
    int Days,
    Guid UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static SalesUploadView Of(SalesUpload upload) => new(
        upload.Id, upload.FileName, upload.Rows, upload.FirstDate, upload.LastDate, upload.Days, upload.UploadedBy, upload.UploadedAt);
}

internal static class SalesRows
{
    public const string Upload = "upload";
    public const string Table = "table";

    public static JsonObject Of(SalesRow row, string source) => new()
    {
        ["date"] = FeedClock.Iso(row.Date),
        ["region"] = row.Region,
        ["orders"] = row.Orders,
        ["revenue"] = row.Revenue.ToString(CultureInfo.InvariantCulture),
        ["source"] = source,
    };
}

internal static class SalesEndpoints
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const string ChooseFile = "Choose a CSV file to upload.";
    public const string TooLarge = "the file is larger than 10 MB";
    public const int ListedUploads = 50;

    public static async Task<Results<Created<SalesUploadView>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        IFormFile? file,
        MeasurementDbContext db,
        BigQueryTables tables,
        AnalyticsOptions options,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AnalyticsOptions.NotSetUp);
        }

        if (file is null)
        {
            return FileProblem([ChooseFile]);
        }

        if (file.Length > MaxFileBytes)
        {
            return FileProblem([TooLarge]);
        }

        SalesFile parsed;
        await using (var stream = file.OpenReadStream())
        {
            parsed = SalesCsv.Parse(stream, FeedClock.Today(time, FeedClock.IndiaTimeZone));
        }

        if (!parsed.IsValid)
        {
            return FileProblem(parsed.Errors);
        }

        var dates = parsed.Rows.Select(row => row.Date).Distinct().Order().ToList();
        await tables.ReplaceDaysAsync(
            member.BrandId,
            TableSchemas.SalesDaily,
            dates,
            parsed.Rows.Select(row => SalesRows.Of(row, SalesRows.Upload)).ToList(),
            cancellationToken);

        var name = Path.GetFileName(file.FileName);
        var upload = new SalesUpload
        {
            Id = Guid.NewGuid(),
            FileName = name.Length == 0 ? "sales.csv" : name.Length > 200 ? name[..200] : name,
            Rows = parsed.Rows.Count,
            FirstDate = dates[0],
            LastDate = dates[^1],
            Days = dates.Count,
            UploadedBy = member.UserId,
            UploadedAt = time.GetUtcNow(),
        };
        db.SalesUploads.Add(upload);
        await db.SaveChangesAsync(cancellationToken);

        var view = SalesUploadView.Of(upload);
        await audit.RecordAsync(new AuditRecord("analytics.sales_uploaded", "sales_upload", upload.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/analytics/sales/uploads", view);
    }

    public static async Task<Ok<SalesUploadView[]>> ListUploadsAsync(MeasurementDbContext db, CancellationToken cancellationToken)
    {
        var uploads = await db.SalesUploads
            .OrderByDescending(upload => upload.UploadedAt)
            .Take(ListedUploads)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(uploads.Select(SalesUploadView.Of).ToArray());
    }

    private static ValidationProblem FileProblem(IReadOnlyList<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = errors.ToArray() });
}
