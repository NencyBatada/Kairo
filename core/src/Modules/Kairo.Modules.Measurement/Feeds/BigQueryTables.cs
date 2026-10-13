using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal static class ReplaceScript
{
    public static string For(string project, string dataset, TableSchema table, string? staging)
    {
        var target = $"`{project}.{dataset}.{table.Name}`";
        var delete = $"DELETE FROM {target} WHERE date IN UNNEST(@dates);";
        if (staging is null)
        {
            return delete;
        }

        var columns = string.Join(", ", table.Columns.Select(column => column.Name));
        var source = $"`{project}.{dataset}.{staging}`";
        return $"""
            BEGIN TRANSACTION;
            {delete}
            INSERT INTO {target} ({columns}) SELECT {columns} FROM {source};
            COMMIT TRANSACTION;
            DROP TABLE {source};
            """;
    }
}

internal sealed class BigQueryTables(
    IHttpClientFactory http,
    BigQueryClient bigQuery,
    IServiceAccessToken serviceToken,
    AnalyticsOptions options,
    TimeProvider time,
    ILogger<BigQueryTables> logger)
{
    private readonly ConcurrentDictionary<string, bool> _ready = new();

    public async Task<int> ReplaceDaysAsync(
        Guid brandId,
        TableSchema table,
        IReadOnlyCollection<DateOnly> dates,
        IReadOnlyList<JsonObject> rows,
        CancellationToken cancellationToken)
    {
        var project = options.Project ?? throw new InvalidOperationException(AnalyticsOptions.NotSetUp);
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        var token = await serviceToken.GetAsync(cancellationToken);
        var client = http.CreateClient(MeasurementHttp.ClientName);
        await EnsureTableAsync(client, token, project, dataset, table, cancellationToken);

        string? staging = null;
        try
        {
            if (rows.Count > 0)
            {
                staging = $"_staging_{table.Name}_{Guid.NewGuid():N}";
                await LoadAsync(client, token, project, dataset, staging, table, rows, cancellationToken);
            }

            await bigQuery.QueryAsync(
                project,
                token,
                ReplaceScript.For(project, dataset, table, staging),
                options.Location,
                [QueryParameter.Dates("dates", dates.Order())],
                cancellationToken);
            return rows.Count;
        }
        catch when (staging is not null)
        {
            await DropQuietlyAsync(client, token, project, dataset, staging);
            throw;
        }
    }

    private async Task EnsureTableAsync(HttpClient client, string token, string project, string dataset, TableSchema table, CancellationToken cancellationToken)
    {
        var datasetKey = $"{project}.{dataset}";
        if (!_ready.ContainsKey(datasetKey))
        {
            await CreateAsync(
                client,
                token,
                $"{BigQueryClient.BaseUrl}/projects/{project}/datasets",
                new { datasetReference = new { projectId = project, datasetId = dataset }, location = options.Location },
                cancellationToken);
            _ready[datasetKey] = true;
        }

        var tableKey = $"{datasetKey}.{table.Name}";
        if (!_ready.ContainsKey(tableKey))
        {
            await CreateAsync(
                client,
                token,
                $"{BigQueryClient.BaseUrl}/projects/{project}/datasets/{dataset}/tables",
                new
                {
                    tableReference = new { projectId = project, datasetId = dataset, tableId = table.Name },
                    schema = table.SchemaJson(),
                    timePartitioning = new { type = "DAY", field = "date" },
                },
                cancellationToken);
            _ready[tableKey] = true;
        }
    }

    private static async Task CreateAsync(HttpClient client, string token, string url, object body, CancellationToken cancellationToken)
    {
        using var response = await client.RequestAsync(HttpMethod.Post, url, token, JsonContent.Create(body, options: MeasurementHttp.Json), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            await MeasurementHttp.ReadAsync(response, cancellationToken);
        }
    }

    private async Task LoadAsync(
        HttpClient client,
        string token,
        string project,
        string dataset,
        string staging,
        TableSchema table,
        IReadOnlyList<JsonObject> rows,
        CancellationToken cancellationToken)
    {
        var loadedAt = time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
        var lines = new StringBuilder();
        foreach (var row in rows)
        {
            row["loaded_at"] = loadedAt;
            lines.Append(row.ToJsonString()).Append('\n');
        }

        var jobId = $"kairo_load_{Guid.NewGuid():N}";
        var job = new
        {
            jobReference = new { projectId = project, jobId, location = options.Location },
            configuration = new
            {
                load = new
                {
                    destinationTable = new { projectId = project, datasetId = dataset, tableId = staging },
                    schema = table.SchemaJson(),
                    sourceFormat = "NEWLINE_DELIMITED_JSON",
                    writeDisposition = "WRITE_TRUNCATE",
                    createDisposition = "CREATE_IF_NEEDED",
                },
            },
        };
        var data = new ByteArrayContent(Encoding.UTF8.GetBytes(lines.ToString()));
        data.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var content = new MultipartContent("related")
        {
            new StringContent(JsonSerializer.Serialize(job, MeasurementHttp.Json), Encoding.UTF8, "application/json"),
            data,
        };

        using var response = await client.RequestAsync(
            HttpMethod.Post, $"{BigQueryClient.UploadUrl}/projects/{project}/jobs?uploadType=multipart", token, content, cancellationToken);
        var state = await MeasurementHttp.ReadAsync(response, cancellationToken);
        while (true)
        {
            var status = state.GetProperty("status");
            if (status.TryGetProperty("errorResult", out var error))
            {
                throw new GoogleApiException(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error }));
            }

            if (status.GetProperty("state").GetString() == "DONE")
            {
                return;
            }

            await Task.Delay(BigQueryClient.PollDelay, cancellationToken);
            state = await client.CallAsync(
                HttpMethod.Get, $"{BigQueryClient.BaseUrl}/projects/{project}/jobs/{jobId}?location={options.Location}", token, null, cancellationToken);
        }
    }

    private async Task DropQuietlyAsync(HttpClient client, string token, string project, string dataset, string staging)
    {
        try
        {
            using var response = await client.RequestAsync(
                HttpMethod.Delete, $"{BigQueryClient.BaseUrl}/projects/{project}/datasets/{dataset}/tables/{staging}", token, null, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove the staging table {Staging} in {Dataset}.", staging, dataset);
        }
    }
}
