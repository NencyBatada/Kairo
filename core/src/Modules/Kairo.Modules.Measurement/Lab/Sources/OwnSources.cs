using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Lab.Sources;

internal static class LabTableRules
{
    public static readonly IReadOnlyDictionary<string, string[]> Types = new Dictionary<string, string[]>
    {
        ["date"] = ["DATE", "DATETIME", "TIMESTAMP"],
        ["value"] = ["INTEGER", "INT64", "NUMERIC", "BIGNUMERIC", "FLOAT", "FLOAT64"],
        ["series"] = ["STRING", "INTEGER", "INT64"],
    };

    public static TableField Field(TableInfo table, string role, string column)
    {
        var field = table.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase))
            ?? throw new LabDataProblem($"The table has no column named {column}.");
        var allowed = Types[role];
        if (field.Mode == "REPEATED" || !allowed.Contains(field.Type) || !SalesColumnRules.IsSafeName(field.Name))
        {
            throw new LabDataProblem($"{column} is a {field.Type} column, but the {role} needs {string.Join(", ", allowed)}.");
        }

        return field;
    }

    public static string Query(string table, TableField date, TableField value, TableField series)
    {
        var day = date.Type == "TIMESTAMP" ? $"DATE(`{date.Name}`, '{FeedClock.IndiaTimeZone}')" : $"DATE(`{date.Name}`)";
        return $"""
            SELECT CAST({day} AS STRING) AS date, CAST(`{series.Name}` AS STRING) AS series, CAST(SUM(`{value.Name}`) AS FLOAT64) AS value
            FROM `{table}`
            WHERE {day} BETWEEN @from AND @to AND `{series.Name}` IS NOT NULL
            GROUP BY 1, 2
            """;
    }
}

internal sealed class BrandTableSource(IConnections connections, BigQueryClient bigQuery, BigQueryTableCheck check, TimeProvider time) : ILabSource
{
    public const string CannotRead = "Kairo can't read this table with the connected Google account.";

    public bool Handles(LabSourceSpec spec) => spec.Kind == LabKinds.BigQuery;

    public Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken) =>
        Task.FromResult<DateOnly?>(LiveData.Yesterday(time));

    public async Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var table = TableRef.Parse(spec.Table) ?? throw new LabDataProblem(SalesEndpoints.BadTable);
        var access = await connections.GetAccessAsync(ConnectedProduct.BigQuery, cancellationToken);
        var info = await check.FindAsync(table, access.AccessToken, cancellationToken) ?? throw new LabDataProblem(CannotRead);
        var sql = LabTableRules.Query(
            table.ToString(),
            LabTableRules.Field(info, "date", spec.DateColumn ?? ""),
            LabTableRules.Field(info, "value", spec.ValueColumn ?? ""),
            LabTableRules.Field(info, "series", spec.SeriesColumn ?? ""));
        var rows = await bigQuery.QueryAsync(
            access.ExternalId, access.AccessToken, sql, info.Location, [QueryParameter.Date("from", from), QueryParameter.Date("to", to)], cancellationToken)
            .OnConnectionAsync();
        var builder = new LabSeriesBuilder(from, to);
        foreach (var row in rows)
        {
            if (DateOnly.TryParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                builder.Add(row[1]?.Trim().ToLowerInvariant(), row[1], date, LabQueries.Number(row[2]));
            }
        }

        return builder.Build(null);
    }
}

internal sealed class FileSource(MeasurementDbContext db) : ILabSource
{
    public const string UploadFirst = "Upload a CSV file first.";

    public bool Handles(LabSourceSpec spec) => spec.Kind == LabKinds.File;

    public async Task<DateOnly?> ThroughAsync(LabSourceSpec spec, Guid analysisId, CancellationToken cancellationToken) =>
        await db.LabDatasets.AsNoTracking().Where(row => row.AnalysisId == analysisId).Select(row => row.UploadTo).SingleOrDefaultAsync(cancellationToken);

    public async Task<LabData> LoadAsync(LabSourceSpec spec, Guid brandId, Guid analysisId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var dataset = await db.LabDatasets.AsNoTracking().SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        if (dataset is not { Upload: { } upload, UploadFrom: { } start })
        {
            throw new LabDataProblem(UploadFirst);
        }

        var builder = new LabSeriesBuilder(from, to);
        foreach (var series in JsonSerializer.Deserialize<LabSeriesData[]>(upload, JsonSerializerOptions.Web) ?? [])
        {
            for (var day = 0; day < series.Values.Length; day++)
            {
                builder.Add(series.Key, series.Name, start.AddDays(day), series.Values[day]);
            }
        }

        return builder.Build(null);
    }
}
