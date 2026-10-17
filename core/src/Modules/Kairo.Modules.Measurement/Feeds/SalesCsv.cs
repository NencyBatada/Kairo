using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record SalesRow(DateOnly Date, string Region, long Orders, decimal Revenue);

internal sealed record SalesFile(IReadOnlyList<SalesRow> Rows, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static partial class SalesCsv
{
    public const int MaxErrors = 20;
    public const int MaxRegionLength = 100;

    private static readonly string[] Columns = ["date", "region", "orders", "revenue"];

    public const string NotText = "the file isn't UTF-8 text";

    public static SalesFile Parse(Stream stream, DateOnly today)
    {
        if (Text(stream) is not { } text)
        {
            return Invalid(NotText);
        }

        var records = Records(text, out var unclosed);
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
            return Invalid("row 1: the header must name date, region, orders and revenue");
        }

        var position = Columns.ToDictionary(column => column, column => Array.IndexOf(header, column));
        var rows = new List<SalesRow>();
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

            var region = values[position["region"]].Trim();
            if (region.Length == 0)
            {
                problems.Add("region is empty");
            }
            else if (region.Length > MaxRegionLength)
            {
                problems.Add($"region is longer than {MaxRegionLength} characters");
            }

            var ordersText = values[position["orders"]].Trim();
            var orders = 0L;
            if (!WholeNumber().IsMatch(ordersText)
                || !long.TryParse(ordersText, NumberStyles.None, CultureInfo.InvariantCulture, out orders))
            {
                problems.Add("orders must be a whole number, 0 or more");
            }

            var revenueText = values[position["revenue"]].Trim();
            var revenue = 0m;
            if (!Money().IsMatch(revenueText)
                || !decimal.TryParse(revenueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out revenue))
            {
                problems.Add("revenue must be a number, 0 or more, with up to 2 decimal places");
            }

            var key = (date, region.ToUpperInvariant());
            if (problems.Count == 0 && seen.TryGetValue(key, out var first))
            {
                problems.Add($"date and region already appear in row {first}");
            }

            foreach (var problem in problems.Take(MaxErrors - errors.Count))
            {
                errors.Add($"row {row}: {problem}");
            }

            if (problems.Count == 0)
            {
                seen[key] = row;
                rows.Add(new SalesRow(date, region, orders, revenue));
            }
        }

        if (errors.Count > 0)
        {
            return new SalesFile([], errors);
        }

        return rows.Count == 0 ? Invalid("the file has no rows") : new SalesFile(rows, []);
    }

    private static SalesFile Invalid(string error) => new([], [error]);

    public static string? Text(Stream stream)
    {
        string text;
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    public static List<List<string>> Records(string text, out bool unclosed)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        unclosed = quoted;
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    [GeneratedRegex("^[0-9]{1,18}$")]
    private static partial Regex WholeNumber();

    [GeneratedRegex(@"^[0-9]{1,20}(\.[0-9]{1,2})?$")]
    private static partial Regex Money();
}
