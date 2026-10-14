using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
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

internal sealed record SalesColumns(string Date, string Region, string Orders, string Revenue);

internal sealed record SalesColumnsRequest(string? Date, string? Region, string? Orders, string? Revenue);

internal sealed record SalesSourceRequest(string? Table, SalesColumnsRequest? Columns);

internal sealed record SalesSourceView(string Table, SalesColumns Columns, string Location, Guid SetBy, DateTimeOffset SetAt)
{
    public static SalesSourceView Of(SalesSource source) => new(
        source.TableRef,
        new SalesColumns(source.DateColumn, source.RegionColumn, source.OrdersColumn, source.RevenueColumn),
        source.Location,
        source.SetBy,
        source.SetAt);
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
    public const long MaxRequestBytes = MaxFileBytes + 64 * 1024;
    public const string ChooseFile = "Choose a CSV file to upload.";
    public const string TooLarge = "the file is larger than 10 MB";
    public const int ListedUploads = 50;
    public const string ConnectBigQuery = "Connect BigQuery before choosing a sales table.";
    public const string BadTable = "Use project.dataset.table, made of letters, digits, underscores and hyphens.";
    public const string CannotRead = "Kairo can't read this table with the connected Google account.";
    public const string BadColumn = "Use a column name made of letters, digits and underscores.";

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

    public static async Task<Results<Ok<SalesSourceView>, NoContent>> GetSourceAsync(MeasurementDbContext db, CancellationToken cancellationToken)
    {
        if (await db.SalesSources.SingleOrDefaultAsync(cancellationToken) is { } source)
        {
            return TypedResults.Ok(SalesSourceView.Of(source));
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<SalesSourceView>, ValidationProblem, ProblemHttpResult>> SetSourceAsync(
        SalesSourceRequest request,
        MeasurementDbContext db,
        IConnections connections,
        BigQueryTableCheck check,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if ((await connections.GetStatusAsync(ConnectedProduct.BigQuery, cancellationToken)).State != ConnectionState.Connected)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ConnectBigQuery);
        }

        var errors = new Dictionary<string, string[]>();
        var table = TableRef.Parse(request.Table);
        if (table is null)
        {
            errors["table"] = [BadTable];
        }

        var columns = new Dictionary<string, string?>
        {
            ["date"] = request.Columns?.Date,
            ["region"] = request.Columns?.Region,
            ["orders"] = request.Columns?.Orders,
            ["revenue"] = request.Columns?.Revenue,
        };
        foreach (var (role, column) in columns)
        {
            if (!SalesColumnRules.IsSafeName(column))
            {
                errors[$"columns.{role}"] = [BadColumn];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        ConnectionAccess access;
        try
        {
            access = await connections.GetAccessAsync(ConnectedProduct.BigQuery, cancellationToken);
        }
        catch (ConnectionUnavailableException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ConnectBigQuery);
        }

        if (await check.FindAsync(table!, access.AccessToken, cancellationToken) is not { } info)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["table"] = [CannotRead] });
        }

        foreach (var (role, column) in columns)
        {
            if (SalesColumnRules.Problem(role, column!, info) is { } problem)
            {
                errors[$"columns.{role}"] = [problem];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var source = await db.SalesSources.SingleOrDefaultAsync(cancellationToken);
        var before = source is null ? null : SalesSourceView.Of(source);
        if (source is null)
        {
            source = new SalesSource { BrandId = member.BrandId };
            db.SalesSources.Add(source);
        }

        source.TableRef = table!.ToString();
        source.DateColumn = columns["date"]!;
        source.DateColumnType = info.Fields.First(field => string.Equals(field.Name, source.DateColumn, StringComparison.OrdinalIgnoreCase)).Type;
        source.RegionColumn = columns["region"]!;
        source.OrdersColumn = columns["orders"]!;
        source.RevenueColumn = columns["revenue"]!;
        source.Location = info.Location;
        source.SetBy = member.UserId;
        source.SetAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var after = SalesSourceView.Of(source);
        await audit.RecordAsync(
            new AuditRecord("analytics.sales_source_set", "sales_source", member.BrandId.ToString(), before, after),
            cancellationToken);
        return TypedResults.Ok(after);
    }

    public static async Task<NoContent> RemoveSourceAsync(
        MeasurementDbContext db,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await db.SalesSources.SingleOrDefaultAsync(cancellationToken) is { } source)
        {
            var before = SalesSourceView.Of(source);
            db.SalesSources.Remove(source);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord("analytics.sales_source_removed", "sales_source", member.BrandId.ToString(), before),
                cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static ValidationProblem FileProblem(IReadOnlyList<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = errors.ToArray() });
}
