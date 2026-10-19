using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Contracts.Data;
using Coworkee.OData.Transfer.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData.Transfer;

public sealed record ExportFile(byte[] Content, string ContentType, string Name);

/// <summary>
/// The rows of a set as its list shows them ($filter, $search, $orderby), with the chosen columns (columns=Name,Price), as
/// Excel, CSV or JSON. Larger exports than <see cref="CoworkeeODataOptions.BackgroundExportRows"/> run as a job.
/// </summary>
public static class DataExport
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static ExportFileFormat Format(string? format) =>
        Enum.TryParse<ExportFileFormat>(format, ignoreCase: true, out var parsed) ? parsed : ExportFileFormat.Xlsx;

    /// <summary>The rows the query string selects; null when there are more than <paramref name="maxRows"/>.</summary>
    internal static async Task<ExportFile?> WriteAsync<TEntity>(IServiceProvider services, string entitySet, QueryString query, int? maxRows, CancellationToken cancellationToken)
        where TEntity : class
    {
        var model = services.GetRequiredService<IEdmModel>();
        var http = new DefaultHttpContext { RequestServices = services };
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        http.Request.QueryString = query;
        var options = http.Request.ODataQueryOptions<TEntity>(model);
        var denied = await ODataEntityAccess.DeniedPropertiesAsync(services, typeof(TEntity), cancellationToken);
        if (await services.GetRequiredService<Computed.ODataComputedProperties>().DeniedAsync(services, typeof(TEntity), cancellationToken,
                http.Request.Query["$filter"], http.Request.Query["$orderby"]))
        {
            throw new UnauthorizedAccessException("The export filters or sorts on a property the user may not read.");
        }

        var rows = (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken)).ApplyODataSearch(options.Search)!
            .ApplyODataFilter(options)
            .ApplyODataOrderBy(options);
        if (maxRows is { } max && await rows.CountAsync(cancellationToken) > max)
        {
            return null;
        }

        var list = await rows.Take(services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.MaxExportRows).ToListAsync(cancellationToken);
        var wanted = http.Request.Query["columns"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = Columns<TEntity>(model, denied);
        var columns = wanted.Length == 0 ? all : [.. wanted.Select(w => all.FirstOrDefault(c => string.Equals(c.Name, w, StringComparison.OrdinalIgnoreCase))).OfType<PropertyInfo>()];
        return Format(http.Request.Query["format"]) switch
        {
            ExportFileFormat.Csv => new ExportFile(Csv(columns, list), "text/csv", $"{entitySet}.csv"),
            ExportFileFormat.Json => new ExportFile(Json(columns, list), "application/json", $"{entitySet}.json"),
            _ => new ExportFile(Xlsx(entitySet, columns, list), XlsxContentType, $"{entitySet}.xlsx"),
        };
    }

    /// <summary>The exportable properties of the EDM type, without <paramref name="denied"/> ones.</summary>
    internal static List<PropertyInfo> Columns<TEntity>(IEdmModel model, IReadOnlySet<string> denied)
    {
        var type = (IEdmStructuredType)model.FindDeclaredType(typeof(TEntity).FullName);
        return
        [
            .. type.StructuralProperties()
                .Where(p => (p.Type.IsPrimitive() || p.Type.IsEnum()) && !denied.Contains(p.Name))
                .Select(p => typeof(TEntity).GetProperty(p.Name))
                .OfType<PropertyInfo>(),
        ];
    }

    private static byte[] Csv<TEntity>(List<PropertyInfo> columns, List<TEntity> rows) =>
        CsvFile.Write([columns.Select(c => c.Name), .. rows.Select(row => columns.Select(c => Text(c.GetValue(row))))]);

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("o", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static byte[] Json<TEntity>(List<PropertyInfo> columns, List<TEntity> rows) =>
        JsonSerializer.SerializeToUtf8Bytes(
            rows.Select(row => columns.ToDictionary(c => c.Name, c => c.GetValue(row))),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

    private static byte[] Xlsx<TEntity>(string entitySet, List<PropertyInfo> columns, List<TEntity> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(entitySet.Length > 31 ? entitySet[..31] : entitySet);
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
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
