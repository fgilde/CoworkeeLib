using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting;

internal static class ReportSheet
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int MaxRows = 100_000;

    public static byte[] Write(ReportSheetRequest request) => Write([request]);

    /// <summary>One worksheet per table, e.g. one per widget of a report.</summary>
    public static byte[] Write(IReadOnlyList<ReportSheetRequest> requests)
    {
        using var workbook = new XLWorkbook();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests)
        {
            var name = SheetName(request.Name);
            for (var i = 2; !names.Add(name); i++)
            {
                name = $"{SheetName(request.Name)[..Math.Min(26, SheetName(request.Name).Length)]} ({i})";
            }

            Fill(workbook.AddWorksheet(name), request);
        }

        if (requests.Count == 0)
        {
            workbook.AddWorksheet("Report");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Fill(IXLWorksheet sheet, ReportSheetRequest request)
    {
        for (var column = 0; column < request.Headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = request.Headers[column];
        }

        for (var row = 0; row < Math.Min(request.Rows.Count, MaxRows); row++)
        {
            for (var column = 0; column < request.Rows[row].Count; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = Cell(request.Rows[row][column]);
            }
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }

    private static XLCellValue Cell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => Blank.Value,
    };

    // Excel allows 31 characters without : \ / ? * [ ]
    private static string SheetName(string name)
    {
        var clean = new string([.. name.Where(c => !@":\/?*[]".Contains(c))]).Trim();
        return clean.Length == 0 ? "Report" : clean[..Math.Min(31, clean.Length)];
    }
}
