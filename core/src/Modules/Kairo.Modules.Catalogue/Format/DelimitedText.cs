using System.Text;

namespace Kairo.Modules.Catalogue.Format;

internal static class DelimitedText
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static List<RawRow> Read(byte[] content, int start)
    {
        var text = Utf8.GetString(content, start, content.Length - start);
        using var records = Records(text).GetEnumerator();
        string[]? header = null;
        while (records.MoveNext())
        {
            if (records.Current.Any(field => field.Trim().Length > 0))
            {
                header = records.Current.Select(Attributes.Normalise).ToArray();
                break;
            }
        }

        if (header is null)
        {
            throw CatalogueFileException.Unreadable();
        }

        if (!header.Contains(Attributes.Id))
        {
            throw CatalogueFileException.NoIdColumn();
        }

        var rows = new List<RawRow>();
        var number = 1;
        while (records.MoveNext())
        {
            number++;
            var row = new RawRow(number);
            var fields = records.Current;
            for (var i = 0; i < fields.Count && i < header.Length; i++)
            {
                if (Attributes.ListValued.Contains(header[i]))
                {
                    foreach (var part in fields[i].Split(','))
                    {
                        row.Add(header[i], part);
                    }
                }
                else
                {
                    row.Add(header[i], fields[i]);
                }
            }

            if (!row.IsEmpty)
            {
                CatalogueFile.CheckCount(rows.Count + 1);
                rows.Add(row);
            }
        }

        return rows;
    }

    private static char DelimiterOf(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text.AsSpan(start, end - start);
            if (!line.IsWhiteSpace())
            {
                return line.Contains('\t') ? '\t' : ',';
            }

            start = end + 1;
        }

        return ',';
    }

    private static IEnumerable<List<string>> Records(string text)
    {
        var delimiter = DelimiterOf(text);
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                    i++;
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i += 2;
                }
                else
                {
                    quoted = false;
                    i++;
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
                i++;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                i++;
            }
            else if (c is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                yield return fields;
                fields = [];
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}
