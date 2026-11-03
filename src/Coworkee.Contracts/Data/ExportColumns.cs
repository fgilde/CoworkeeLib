namespace Coworkee.Contracts.Data;

/// <summary>
/// The <c>columns</c> parameter of an export: the fields in order, comma-separated, each with an optional header after a colon
/// (<c>Number:Shipment no.,Total</c>); a backslash escapes the next character, e.g. a comma in a header (<c>Weight:Weight\, kg</c>).
/// Without a header the column is headed by its field name. CSV and Excel show the headers; JSON keys stay the field names.
/// </summary>
public static class ExportColumns
{
    /// <summary>The value of <c>columns</c> for these fields and headers (null: the field name), escaped.</summary>
    public static string Format(IEnumerable<(string Field, string? Header)> columns) =>
        string.Join(',', columns.Select(c => string.IsNullOrEmpty(c.Header) ? Escape(c.Field) : $"{Escape(c.Field)}:{Escape(c.Header)}"));

    /// <summary>The fields and headers of a <c>columns</c> value; empty entries are skipped.</summary>
    public static IReadOnlyList<(string Field, string? Header)> Parse(string? value)
    {
        var columns = new List<(string Field, string? Header)>();
        var part = new System.Text.StringBuilder();
        string? field = null;
        value ??= string.Empty;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == ',')
            {
                var text = part.ToString().Trim();
                if ((field ?? text).Length > 0)
                {
                    columns.Add(field is null ? (text, null) : (field, text.Length > 0 ? text : null));
                }

                (field, part.Length) = (null, 0);
            }
            else if (value[i] == '\\' && i + 1 < value.Length)
            {
                part.Append(value[++i]);
            }
            else if (value[i] == ':' && field is null)
            {
                (field, part.Length) = (part.ToString().Trim(), 0);
            }
            else
            {
                part.Append(value[i]);
            }
        }

        return columns;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace(",", "\\,").Replace(":", "\\:");
}
