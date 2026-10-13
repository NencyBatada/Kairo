using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kairo.IntegrationTests.Api;

public sealed record FakeQuery(string Project, string AccessToken, string Location, string Sql, JsonElement Parameters);

public sealed record BrandTable(string Location, (string Name, string Type)[] Fields);

public sealed record SalesTableRow(string Date, string Region, string Orders, string Revenue);

public sealed partial class FakeBigQuery
{
    public const string KairoProject = "kairo-analytics-test";
    public const string BrandProject = "acme-data";

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, string> _datasets = new();
    private readonly Dictionary<string, List<JsonObject>> _tables = new();
    private readonly ConcurrentDictionary<string, Queue<object[]>> _pages = new();

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<JsonObject> TableInserts { get; } = new();

    public ConcurrentQueue<FakeQuery> Queries { get; } = new();

    public bool FailScripts { get; set; }

    public HttpStatusCode BrandTableRead { get; set; } = HttpStatusCode.OK;

    public ConcurrentDictionary<string, BrandTable> BrandTables { get; } = new()
    {
        ["shop.daily_orders"] = new BrandTable(
            "asia-south1",
            [("order_date", "DATE"), ("state", "STRING"), ("order_count", "INTEGER"), ("net_revenue", "NUMERIC"), ("notes", "STRING")]),
    };

    public List<SalesTableRow> SalesTableRows { get; } = [];

    public int SalesPageSize { get; set; } = 2;

    public int Count(string call) => Calls.Count(entry => entry == call);

    public string? DatasetLocation(string dataset) => _datasets.GetValueOrDefault(dataset);

    public IReadOnlyList<string> TableNames(string dataset)
    {
        lock (_lock)
        {
            return _tables.Keys.Where(key => key.StartsWith(dataset + ".", StringComparison.Ordinal)).Select(key => key[(dataset.Length + 1)..]).ToList();
        }
    }

    public IReadOnlyList<JsonObject> Rows(string dataset, string table)
    {
        lock (_lock)
        {
            return _tables.TryGetValue($"{dataset}.{table}", out var rows) ? rows.Select(row => (JsonObject)row.DeepClone()).ToList() : [];
        }
    }

    public HttpResponseMessage? Handle(HttpRequestMessage request, string body, string accessToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var upload = path.StartsWith("/upload/", StringComparison.Ordinal);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(upload ? 4 : 3).ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        var project = parts[0];
        var rest = parts[1..];
        var method = request.Method.Method;
        if (project == KairoProject)
        {
            if (accessToken != FakeGoogleApis.ServiceToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return (method, rest) switch
            {
                ("POST", ["datasets"]) => InsertDataset(body),
                ("POST", ["datasets", var dataset, "tables"]) => InsertTable(dataset, body),
                ("POST", ["jobs"]) when upload => Load(request, body),
                ("GET", ["jobs", var jobId]) => Json(HttpStatusCode.OK, new { jobReference = new { jobId }, status = new { state = "DONE" } }),
                ("POST", ["queries"]) => Script(body),
                ("DELETE", ["datasets", var dataset, "tables", var table]) => DeleteTable(dataset, table),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        if (project == BrandProject)
        {
            if (accessToken == FakeGoogleApis.ServiceToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return (method, rest) switch
            {
                ("GET", ["datasets", var dataset, "tables", var table]) => BrandTableOf(dataset, table),
                ("POST", ["queries"]) => SalesQuery(accessToken, body),
                ("GET", ["queries", var jobId]) => NextPage(jobId),
                _ => null,
            };
        }

        return null;
    }

    private HttpResponseMessage InsertDataset(string body)
    {
        var request = JsonNode.Parse(body)!;
        var dataset = request["datasetReference"]!["datasetId"]!.GetValue<string>();
        Calls.Enqueue($"datasets.insert {dataset}");
        return _datasets.TryAdd(dataset, request["location"]?.GetValue<string>() ?? "US")
            ? Json(HttpStatusCode.OK, new { id = $"{KairoProject}:{dataset}" })
            : Json(HttpStatusCode.Conflict, new { error = new { message = "Already Exists: Dataset" } });
    }

    private HttpResponseMessage InsertTable(string dataset, string body)
    {
        var request = JsonNode.Parse(body)!.AsObject();
        var table = request["tableReference"]!["tableId"]!.GetValue<string>();
        Calls.Enqueue($"tables.insert {dataset}.{table}");
        TableInserts.Enqueue(request);
        if (!_datasets.ContainsKey(dataset))
        {
            return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Dataset" } });
        }

        lock (_lock)
        {
            if (!_tables.TryAdd($"{dataset}.{table}", []))
            {
                return Json(HttpStatusCode.Conflict, new { error = new { message = "Already Exists: Table" } });
            }
        }

        return Json(HttpStatusCode.OK, new { id = $"{KairoProject}:{dataset}.{table}" });
    }

    private HttpResponseMessage Load(HttpRequestMessage request, string body)
    {
        var boundary = request.Content!.Headers.ContentType!.Parameters.Single(parameter => parameter.Name == "boundary").Value!.Trim('"');
        var sections = body.Split("--" + boundary)
            .Select(section => section.Trim('\r', '\n'))
            .Where(section => section.Length > 0 && section != "--")
            .Select(section => section[(section.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..])
            .ToList();
        var job = JsonNode.Parse(sections[0])!;
        var destination = job["configuration"]!["load"]!["destinationTable"]!;
        var dataset = destination["datasetId"]!.GetValue<string>();
        var table = destination["tableId"]!.GetValue<string>();
        Calls.Enqueue($"load {dataset}.{table}");
        if (!_datasets.ContainsKey(dataset))
        {
            return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Dataset" } });
        }

        var rows = sections.Count < 2
            ? []
            : sections[1].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonNode.Parse(line)!.AsObject())
                .ToList();
        lock (_lock)
        {
            _tables[$"{dataset}.{table}"] = rows;
        }

        return Json(HttpStatusCode.OK, new { jobReference = job["jobReference"], status = new { state = "DONE" } });
    }

    private HttpResponseMessage Script(string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        var sql = request.GetProperty("query").GetString()!;
        Queries.Enqueue(new FakeQuery(KairoProject, FakeGoogleApis.ServiceToken, request.GetProperty("location").GetString()!, sql, request.GetProperty("queryParameters").Clone()));
        if (FailScripts)
        {
            return Json(HttpStatusCode.BadRequest, new { error = new { message = "Quota exceeded" } });
        }

        var target = DeleteTarget().Match(sql);
        if (!target.Success)
        {
            return Json(HttpStatusCode.BadRequest, new { error = new { message = "Unexpected script" } });
        }

        var dataset = target.Groups["dataset"].Value;
        var dates = DateValues(request, "dates").ToHashSet();
        var staging = StagingSource().Match(sql);
        lock (_lock)
        {
            if (!_tables.TryGetValue($"{dataset}.{target.Groups["table"].Value}", out var rows))
            {
                return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Table" } });
            }

            rows.RemoveAll(row => dates.Contains(row["date"]!.GetValue<string>()));
            if (staging.Success)
            {
                var stagingKey = $"{dataset}.{staging.Groups["table"].Value}";
                rows.AddRange(_tables[stagingKey]);
                _tables.Remove(stagingKey);
            }
        }

        return Json(HttpStatusCode.OK, new { jobComplete = true, jobReference = new { projectId = KairoProject, jobId = $"script_{Guid.NewGuid():N}" } });
    }

    private HttpResponseMessage DeleteTable(string dataset, string table)
    {
        Calls.Enqueue($"tables.delete {dataset}.{table}");
        lock (_lock)
        {
            return _tables.Remove($"{dataset}.{table}") ? new HttpResponseMessage(HttpStatusCode.NoContent) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private HttpResponseMessage BrandTableOf(string dataset, string table)
    {
        if (BrandTableRead != HttpStatusCode.OK)
        {
            return Json(BrandTableRead, new { error = new { message = "Access Denied" } });
        }

        return BrandTables.TryGetValue($"{dataset}.{table}", out var found)
            ? Json(HttpStatusCode.OK, new
            {
                tableReference = new { projectId = BrandProject, datasetId = dataset, tableId = table },
                location = found.Location,
                schema = new { fields = found.Fields.Select(field => new { name = field.Name, type = field.Type, mode = "NULLABLE" }).ToArray() },
            })
            : Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Table" } });
    }

    private HttpResponseMessage SalesQuery(string accessToken, string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        Queries.Enqueue(new FakeQuery(BrandProject, accessToken, request.GetProperty("location").GetString()!, request.GetProperty("query").GetString()!, request.GetProperty("queryParameters").Clone()));
        var from = DateValues(request, "from").Single();
        var to = DateValues(request, "to").Single();
        var rows = SalesTableRows
            .Where(row => string.CompareOrdinal(row.Date, from) >= 0 && string.CompareOrdinal(row.Date, to) <= 0)
            .Select(row => (object)new { f = new object[] { new { v = row.Date }, new { v = row.Region }, new { v = row.Orders }, new { v = row.Revenue } } })
            .Chunk(SalesPageSize);
        return Page($"sales_{Guid.NewGuid():N}", new Queue<object[]>(rows));
    }

    private HttpResponseMessage NextPage(string jobId) =>
        _pages.TryRemove(jobId, out var pages) ? Page(jobId, pages) : Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Job" } });

    private HttpResponseMessage Page(string jobId, Queue<object[]> pages)
    {
        var page = pages.Count > 0 ? pages.Dequeue() : [];
        string? pageToken = null;
        if (pages.Count > 0)
        {
            pageToken = $"page-{pages.Count}";
            _pages[jobId] = pages;
        }

        return Json(HttpStatusCode.OK, new { jobComplete = true, jobReference = new { projectId = BrandProject, jobId }, rows = page, pageToken });
    }

    private static IEnumerable<string> DateValues(JsonElement request, string name)
    {
        var parameter = request.GetProperty("queryParameters").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == name);
        var value = parameter.GetProperty("parameterValue");
        return value.TryGetProperty("arrayValues", out var values)
            ? values.EnumerateArray().Select(entry => entry.GetProperty("value").GetString()!).ToList()
            : [value.GetProperty("value").GetString()!];
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    [GeneratedRegex(@"DELETE FROM `(?<project>[^.`]+)\.(?<dataset>[^.`]+)\.(?<table>[^`]+)` WHERE date IN UNNEST\(@dates\);")]
    private static partial Regex DeleteTarget();

    [GeneratedRegex(@"FROM `[^.`]+\.[^.`]+\.(?<table>_staging_[^`]+)`;")]
    private static partial Regex StagingSource();
}
