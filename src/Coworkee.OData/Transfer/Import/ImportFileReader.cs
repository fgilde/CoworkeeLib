using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Contracts.Data;

namespace Coworkee.OData.Transfer.Import;

/// <summary>Turns an uploaded CSV, Excel or JSON file into text cells; the first row is the header unless it looks like data.</summary>
public static class ImportFileReader
{
    public static readonly string[] Extensions = [".csv", ".txt", ".tsv", ".xlsx", ".json"];

    public static ImportTable Read(byte[] content, string fileName, ImportParseOptions? options = null)
    {
        options ??= new ImportParseOptions();
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => WithHeader(Xlsx(content), typed: true, options.HasHeader),
            ".json" => Json(content),
            _ => Csv(content, options),
        };
    }

    private static ImportTable Csv(byte[] content, ImportParseOptions options)
    {
        var encoding = options.Encoding is { Length: > 0 } name ? CsvFile.EncodingByName(name) : CsvFile.DetectEncoding(content);
        var text = encoding.GetString(content).TrimStart((char)0xFEFF);
        var delimiter = options.Delimiter is { Length: > 0 } chosen ? Unescape(chosen) : CsvFile.DetectDelimiter(text);
        var table = WithHeader(CsvFile.Parse(text, delimiter), typed: false, options.HasHeader);
        return table with { Delimiter = delimiter == '\t' ? "\\t" : delimiter.ToString(), Encoding = encoding.WebName };
    }

    private static char Unescape(string delimiter) => delimiter == "\\t" ? '\t' : delimiter[0];

    private static List<string[]> Xlsx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);
        if (workbook.Worksheet(1).RangeUsed() is not { } used)
        {
            return [];
        }

        var width = used.ColumnCount();
        return [.. used.RowsUsed().Select(row => Enumerable.Range(1, width).Select(column => Text(row.Cell(column))).ToArray())];
    }

    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Number => value.GetNumber().ToString("R", CultureInfo.InvariantCulture),
            XLDataType.DateTime => value.GetDateTime().ToString(value.GetDateTime().TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
            _ => cell.GetFormattedString(),
        };
    }

    private static ImportTable Json(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        var headers = items.Where(i => i.ValueKind == JsonValueKind.Object).SelectMany(i => i.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.Ordinal).ToList();
        var rows = items.Where(i => i.ValueKind == JsonValueKind.Object)
            .Select(item => headers.Select(h => item.TryGetProperty(h, out var value) ? Text(value) : null).ToArray())
            .ToList();
        return new ImportTable(headers, rows, Typed: true, HasHeader: true);
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    private static ImportTable WithHeader(List<string[]> rows, bool typed, bool? hasHeader)
    {
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var header = hasHeader ?? (rows.Count > 0 && LooksLikeHeader(rows[0], width));
        var headers = header
            ? rows[0].Select((h, i) => h.Trim() is { Length: > 0 } text ? text : Column(i)).Concat(Enumerable.Range(rows[0].Length, width - rows[0].Length).Select(Column)).ToList()
            : Enumerable.Range(0, width).Select(Column).ToList();
        var data = rows.Skip(header ? 1 : 0).Select(r => Enumerable.Range(0, width).Select(i => i < r.Length && r[i].Length > 0 ? r[i] : null).ToArray()).ToList();
        return new ImportTable(headers, data, typed, header);
    }

    private static string Column(int index) => $"Column {index + 1}";

    // a header is text all through, without numbers or dates, and names every column once
    private static bool LooksLikeHeader(string[] row, int width) =>
        row.Length == width
        && row.All(cell => cell.Trim().Length > 0
                           && !double.TryParse(cell, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                           && !DateTime.TryParse(cell, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        && row.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == row.Length;
}
