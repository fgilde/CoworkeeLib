using System.Globalization;
using System.Text;

namespace Coworkee.Cli.Spec.Import;

/// <summary>A CSV file becomes one entity: the header names the properties, the first rows tell their types.</summary>
public static class CsvImporter
{
    private const int SampleRows = 500;
    private static readonly int[] Lengths = [50, 100, 200, 500, 1000, 4000];
    private static readonly string[] Bools = ["true", "false", "yes", "no"];

    public static ImportResult Import(ImportRequest request)
    {
        string text;
        try
        {
            text = File.ReadAllText(request.Source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImportException($"Cannot read {request.Source}: {exception.Message}");
        }

        var rows = Parse(text, Delimiter(text)).Take(SampleRows + 1).ToList();
        if (rows.Count == 0 || rows[0].All(string.IsNullOrWhiteSpace))
        {
            throw new ImportException($"{request.Source} has no header row.");
        }

        var file = ImportNames.Pascal(Path.GetFileNameWithoutExtension(request.Source));
        var name = ImportNames.TypeName(ImportNames.Singular(file), ImportNames.Set());
        var entity = new EntitySpec { Name = name, Plural = file != name && file != EntitySpec.Pluralize(name) && SpecValidator.IsSimpleName(file) ? file : null };
        var members = ImportNames.Set();
        var skipped = new List<string>();
        var samples = rows.Skip(1).ToList();
        foreach (var (header, index) in rows[0].Select((h, i) => (h, i)))
        {
            var pascal = string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : ImportNames.Pascal(header);
            if (ImportNames.IsBase(pascal))
            {
                skipped.Add($"{header}: left out; every entity has Id, tenant and audit fields.");
                continue;
            }

            var values = samples.Select(r => index < r.Count ? r[index].Trim() : string.Empty).ToList();
            var filled = values.Where(v => v.Length > 0).ToList();
            var property = Infer(filled, pascal);
            property.Name = ImportNames.Member(pascal, name, members);
            property.Required = samples.Count > 0 && filled.Count == values.Count;
            entity.Properties.Add(property);
        }

        if (samples.Count == 0)
        {
            skipped.Add("No data rows: every column is an optional string.");
        }

        return new ImportResult(new ModuleSpec { Name = request.Module ?? file, Entities = [entity] }, [], skipped);
    }

    private static PropertySpec Infer(List<string> values, string name)
    {
        bool All(Func<string, bool> test) => values.Count > 0 && values.All(test);
        var invariant = CultureInfo.InvariantCulture;
        var type =
            All(v => Bools.Contains(v, StringComparer.OrdinalIgnoreCase)) ? PropertyType.Bool
            : All(v => int.TryParse(v, NumberStyles.AllowLeadingSign, invariant, out _)) ? PropertyType.Int
            : All(v => long.TryParse(v, NumberStyles.AllowLeadingSign, invariant, out _)) ? PropertyType.Long
            : All(v => decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, invariant, out _)) ? PropertyType.Decimal
            : All(v => Guid.TryParse(v, out _)) ? PropertyType.Guid
            : All(v => DateOnly.TryParseExact(v, "yyyy-MM-dd", invariant, DateTimeStyles.None, out _)) ? PropertyType.DateOnly
            : All(v => TimeOnly.TryParseExact(v, ["HH:mm", "HH:mm:ss"], invariant, DateTimeStyles.None, out _)) ? PropertyType.TimeOnly
            : All(v => v.Length > 10 && DateTime.TryParse(v, invariant, DateTimeStyles.RoundtripKind, out _)) ? PropertyType.DateTime
            : All(v => v.Contains('@') && !v.Contains(' ') && v.IndexOf('@') > 0 && v.LastIndexOf('.') > v.IndexOf('@')) ? PropertyType.Email
            : All(v => Uri.TryCreate(v, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") ? PropertyType.Url
            : PropertyType.String;
        var longest = values.Count == 0 ? 0 : values.Max(v => v.Length);
        return new PropertySpec
        {
            Type = type,
            // room to grow: the smallest common length at least twice the longest sample
            MaxLength = type is PropertyType.String or PropertyType.Email or PropertyType.Url && longest > 0 ? Lengths.Cast<int?>().FirstOrDefault(l => l >= longest * 2) : null,
            Precision = type == PropertyType.Decimal ? 18 : null,
            Scale = type == PropertyType.Decimal ? Math.Clamp(values.Max(v => v.Contains('.') ? v.Length - v.IndexOf('.') - 1 : 0), 2, 6) : null,
        };
    }

    private static char Delimiter(string text)
    {
        var header = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t', '|' }.OrderByDescending(d => header.Count(c => c == d)).First();
    }

    // RFC 4180: quoted fields may hold the delimiter, line breaks and doubled quotes
    private static IEnumerable<List<string>> Parse(string text, char delimiter)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
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
                if (row.Any(f => f.Length > 0))
                {
                    yield return row;
                }

                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0))
        {
            yield return row;
        }
    }
}
