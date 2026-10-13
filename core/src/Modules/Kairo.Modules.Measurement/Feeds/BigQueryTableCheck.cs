using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed partial record TableRef(string Project, string Dataset, string Table)
{
    public static TableRef? Parse(string? value)
    {
        var match = Pattern().Match(value ?? "");
        return match.Success
            ? new TableRef(match.Groups["project"].Value, match.Groups["dataset"].Value, match.Groups["table"].Value)
            : null;
    }

    public override string ToString() => $"{Project}.{Dataset}.{Table}";

    [GeneratedRegex("^(?<project>[A-Za-z0-9_-]+)\\.(?<dataset>[A-Za-z0-9_-]+)\\.(?<table>[A-Za-z0-9_]+)$")]
    private static partial Regex Pattern();
}

internal sealed record TableField(string Name, string Type, string Mode);

internal sealed record TableInfo(string Location, IReadOnlyList<TableField> Fields);

internal sealed class BigQueryTableCheck(IHttpClientFactory http)
{
    public async Task<TableInfo?> FindAsync(TableRef table, string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(MeasurementHttp.ClientName).RequestAsync(
            HttpMethod.Get,
            $"{BigQueryClient.BaseUrl}/projects/{table.Project}/datasets/{table.Dataset}/tables/{table.Table}",
            accessToken,
            null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = await MeasurementHttp.ReadAsync(response, cancellationToken);
        var fields = new List<TableField>();
        if (resource.TryGetProperty("schema", out var schema) && schema.TryGetProperty("fields", out var list))
        {
            fields.AddRange(list.EnumerateArray().Select(field => new TableField(
                field.GetProperty("name").GetString()!,
                field.GetProperty("type").GetString()!,
                field.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString()! : "NULLABLE")));
        }

        return new TableInfo(resource.GetProperty("location").GetString()!, fields);
    }
}

internal static partial class SalesColumnRules
{
    public static readonly IReadOnlyDictionary<string, string[]> Types = new Dictionary<string, string[]>
    {
        ["date"] = ["DATE", "DATETIME", "TIMESTAMP"],
        ["region"] = ["STRING"],
        ["orders"] = ["INTEGER", "INT64", "NUMERIC", "BIGNUMERIC"],
        ["revenue"] = ["NUMERIC", "BIGNUMERIC", "FLOAT", "FLOAT64", "INTEGER", "INT64"],
    };

    public static bool IsSafeName(string? name) => name is not null && ColumnName().IsMatch(name);

    public static string? Problem(string role, string column, TableInfo table)
    {
        var field = table.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return $"The table has no column named {column}.";
        }

        var allowed = Types[role];
        return field.Mode != "REPEATED" && allowed.Contains(field.Type)
            ? null
            : $"{column} is a {field.Type} column, but {role} needs {Readable(allowed)}.";
    }

    private static string Readable(string[] types) =>
        types.Length == 1 ? types[0] : $"{string.Join(", ", types[..^1])} or {types[^1]}";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ColumnName();
}
