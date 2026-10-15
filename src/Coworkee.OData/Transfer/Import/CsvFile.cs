using System.Text;

namespace Coworkee.OData.Transfer.Import;

/// <summary>Reads delimited text: encoding from the byte order mark or by trying UTF-8, delimiter by the most even split of the first lines.</summary>
public static class CsvFile
{
    private static readonly char[] Delimiters = [';', ',', '\t', '|'];

    static CsvFile() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return new UTF8Encoding(true);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return System.Text.Encoding.Unicode;
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return System.Text.Encoding.BigEndianUnicode;
        }

        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            // ponytail: anything that is not UTF-8 is read as Windows-1252, the usual Excel CSV of western Europe; pick the encoding for others
            return System.Text.Encoding.GetEncoding(1252);
        }
    }

    public static Encoding EncodingByName(string name) =>
        name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ? new UTF8Encoding(false) : System.Text.Encoding.GetEncoding(name);

    public static char DetectDelimiter(string text)
    {
        var lines = text.Split('\n', 11).Take(10).Where(l => l.Trim().Length > 0).ToList();
        return Delimiters
            .Select(d => (Delimiter: d, Counts: lines.Select(l => Count(l, d)).ToList()))
            .Where(c => c.Counts.Count > 0 && c.Counts[0] > 0)
            .OrderByDescending(c => c.Counts.All(n => n == c.Counts[0]))
            .ThenByDescending(c => c.Counts[0])
            .Select(c => c.Delimiter)
            .DefaultIfEmpty(';')
            .First();
    }

    /// <summary>RFC 4180: quoted fields may hold delimiters, line breaks and doubled quotes.</summary>
    public static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                AddRow(rows, row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        row.Add(field.ToString());
        AddRow(rows, row);
        return rows;
    }

    /// <summary>Semicolon separated UTF-8 with byte order mark, so Excel opens it; cells that start like a formula get a leading quote.</summary>
    public static byte[] Write(IEnumerable<IEnumerable<string?>> rows)
    {
        var csv = new StringBuilder();
        foreach (var row in rows)
        {
            csv.AppendJoin(';', row.Select(Escape)).Append("\r\n");
        }

        return [.. new UTF8Encoding(true).GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        value = value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _) ? "'" + value : value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        if (row.Any(f => f.Length > 0))
        {
            rows.Add([.. row]);
        }
    }

    // delimiters inside quotes do not count
    private static int Count(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var c in line)
        {
            quoted ^= c == '"';
            count += !quoted && c == delimiter ? 1 : 0;
        }

        return count;
    }
}
