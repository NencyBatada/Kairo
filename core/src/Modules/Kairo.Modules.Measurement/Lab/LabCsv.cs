using System.Globalization;
using System.Text.RegularExpressions;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Lab;

internal sealed record LabFile(LabData? Data, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static partial class LabCsv
{
    public const int MaxErrors = 20;
    public const int MaxSpanDays = 1096;

    private static readonly string[] Columns = ["date", "series", "value"];

    public static LabFile Parse(Stream stream, DateOnly today)
    {
        if (SalesCsv.Text(stream) is not { } text)
        {
            return Invalid(SalesCsv.NotText);
        }

        var records = SalesCsv.Records(text, out var unclosed);
        if (unclosed)
        {
            return Invalid($"row {records.Count}: a quoted value is never closed");
        }

        if (records.Count == 0)
        {
            return Invalid("the file is empty");
        }

        var header = records[0].Select(name => name.Trim().ToLowerInvariant()).ToArray();
        if (header.Length != Columns.Length || !Columns.All(column => Array.IndexOf(header, column) >= 0))
        {
            return Invalid("row 1: the header must name date, series and value");
        }

        var position = Columns.ToDictionary(column => column, column => Array.IndexOf(header, column));
        var rows = new List<(DateOnly Date, string Series, double Value)>();
        var errors = new List<string>();
        var seen = new Dictionary<(DateOnly, string), int>();
        for (var index = 1; index < records.Count && errors.Count < MaxErrors; index++)
        {
            var values = records[index];
            var row = index + 1;
            if (values.All(value => value.Trim().Length == 0))
            {
                continue;
            }

            if (values.Count != Columns.Length)
            {
                errors.Add($"row {row}: expected {Columns.Length} values but found {values.Count}");
                continue;
            }

            var problems = new List<string>();
            if (!DateOnly.TryParseExact(values[position["date"]].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                problems.Add("date must be YYYY-MM-DD");
            }
            else if (date > today)
            {
                problems.Add("date can't be after today");
            }

            var series = values[position["series"]].Trim();
            if (series.Length == 0)
            {
                problems.Add("series is empty");
            }
            else if (series.Length > LabSeriesBuilder.KeyLength)
            {
                problems.Add($"series is longer than {LabSeriesBuilder.KeyLength} characters");
            }

            var valueText = values[position["value"]].Trim();
            var value = 0d;
            if (!Number().IsMatch(valueText) || !double.TryParse(valueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            {
                problems.Add("value must be a number, 0 or more");
            }

            var key = (date, series.ToLowerInvariant());
            if (problems.Count == 0 && seen.TryGetValue(key, out var first))
            {
                problems.Add($"date and series already appear in row {first}");
            }

            foreach (var problem in problems.Take(MaxErrors - errors.Count))
            {
                errors.Add($"row {row}: {problem}");
            }

            if (problems.Count == 0)
            {
                seen[key] = row;
                rows.Add((date, series, value));
            }
        }

        if (errors.Count > 0)
        {
            return new LabFile(null, errors);
        }

        if (rows.Count == 0)
        {
            return Invalid("the file has no rows");
        }

        var from = rows.Min(row => row.Date);
        var to = rows.Max(row => row.Date);
        if (to.DayNumber - from.DayNumber + 1 > MaxSpanDays)
        {
            return Invalid("the file covers more than 3 years");
        }

        var builder = new LabSeriesBuilder(from, to);
        foreach (var (rowDate, rowSeries, rowValue) in rows)
        {
            builder.Add(rowSeries.ToLowerInvariant(), rowSeries, rowDate, rowValue);
        }

        try
        {
            return new LabFile(builder.Build(null), []);
        }
        catch (LabDataProblem)
        {
            return Invalid("every value in the file is 0");
        }
    }

    private static LabFile Invalid(string error) => new(null, [error]);

    [GeneratedRegex(@"^[0-9]{1,15}(\.[0-9]{1,6})?$")]
    private static partial Regex Number();
}
