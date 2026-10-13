using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record QueryParameter(string Name, string Type, string? Value = null, IReadOnlyList<string>? Values = null)
{
    public static QueryParameter Date(string name, DateOnly value) => new(name, "DATE", Iso(value));

    public static QueryParameter Dates(string name, IEnumerable<DateOnly> values) => new(name, "DATE", Values: values.Select(Iso).ToList());

    public object ToJson() => Values is null
        ? new { name = Name, parameterType = new { type = Type }, parameterValue = new { value = Value } }
        : new
        {
            name = Name,
            parameterType = new { type = "ARRAY", arrayType = new { type = Type } },
            parameterValue = new { arrayValues = Values.Select(value => new { value }).ToArray() },
        };

    private static string Iso(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed class BigQueryClient(IHttpClientFactory http)
{
    public const string BaseUrl = "https://bigquery.googleapis.com/bigquery/v2";
    public const string UploadUrl = "https://bigquery.googleapis.com/upload/bigquery/v2";

    public static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<string?[]>> QueryAsync(
        string project,
        string accessToken,
        string sql,
        string location,
        IReadOnlyList<QueryParameter> parameters,
        CancellationToken cancellationToken)
    {
        var client = http.CreateClient(MeasurementHttp.ClientName);
        var projectUrl = $"{BaseUrl}/projects/{Uri.EscapeDataString(project)}";
        var page = await client.CallAsync(
            HttpMethod.Post,
            $"{projectUrl}/queries",
            accessToken,
            new
            {
                query = sql,
                useLegacySql = false,
                location,
                parameterMode = "NAMED",
                queryParameters = parameters.Select(parameter => parameter.ToJson()).ToArray(),
                timeoutMs = 60000,
            },
            cancellationToken);
        var jobId = page.GetProperty("jobReference").GetProperty("jobId").GetString()!;
        var rows = new List<string?[]>();
        while (true)
        {
            if (page.TryGetProperty("jobComplete", out var complete) && complete.ValueKind == JsonValueKind.False)
            {
                await Task.Delay(PollDelay, cancellationToken);
                page = await client.CallAsync(HttpMethod.Get, ResultsUrl(projectUrl, jobId, location, null), accessToken, null, cancellationToken);
                continue;
            }

            if (page.TryGetProperty("rows", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(list.EnumerateArray().Select(row => row.GetProperty("f").EnumerateArray()
                    .Select(cell => cell.GetProperty("v") is { ValueKind: JsonValueKind.String } value ? value.GetString() : null)
                    .ToArray()));
            }

            if (!page.TryGetProperty("pageToken", out var token) || token.ValueKind != JsonValueKind.String || token.GetString() is not { Length: > 0 } next)
            {
                return rows;
            }

            page = await client.CallAsync(HttpMethod.Get, ResultsUrl(projectUrl, jobId, location, next), accessToken, null, cancellationToken);
        }
    }

    private static string ResultsUrl(string projectUrl, string jobId, string location, string? pageToken) =>
        QueryHelpers.AddQueryString(
            $"{projectUrl}/queries/{Uri.EscapeDataString(jobId)}",
            new Dictionary<string, string?> { ["location"] = location, ["timeoutMs"] = "60000", ["pageToken"] = pageToken });
}
