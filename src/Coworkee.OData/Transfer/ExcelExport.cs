using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData.Transfer;

internal static class ExcelExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static async Task<IResult> WriteAsync<TEntity>(HttpContext http, string entitySet)
        where TEntity : class
    {
        var services = http.RequestServices;
        var cancellationToken = http.RequestAborted;
        if (!await ODataEntityAccess.IsGrantedAsync(services, typeof(TEntity), cancellationToken))
        {
            return Results.Forbid();
        }

        var model = services.GetRequiredService<IEdmModel>();
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        var options = http.Request.ODataQueryOptions<TEntity>(model);
        var query = (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken)).ApplyODataSearch(options.Search)!
            .ApplyODataFilter(options)
            .ApplyODataOrderBy(options);
        var rows = await query.Take(services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.MaxExportRows).ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        Fill(workbook.AddWorksheet(entitySet), Columns<TEntity>(model), rows);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Results.File(stream.ToArray(), ContentType, $"{entitySet}.xlsx");
    }

    private static void Fill<TEntity>(IXLWorksheet sheet, IReadOnlyList<System.Reflection.PropertyInfo> columns, IReadOnlyList<TEntity> rows)
    {
        for (var column = 0; column < columns.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = columns[column].Name;
            for (var row = 0; row < rows.Count; row++)
            {
                sheet.Cell(row + 2, column + 1).Value = Cell(columns[column].GetValue(rows[row]));
            }
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }

    private static List<System.Reflection.PropertyInfo> Columns<TEntity>(IEdmModel model)
    {
        var type = (IEdmStructuredType)model.FindDeclaredType(typeof(TEntity).FullName);
        return
        [
            .. type.StructuralProperties()
                .Where(p => p.Type.IsPrimitive() || p.Type.IsEnum())
                .Select(p => typeof(TEntity).GetProperty(p.Name))
                .OfType<System.Reflection.PropertyInfo>(),
        ];
    }

    private static XLCellValue Cell(object? value) => value switch
    {
        null => Blank.Value,
        string text => text,
        bool flag => flag,
        DateTime date => date,
        DateTimeOffset date => date.UtcDateTime,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        TimeSpan span => span,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
