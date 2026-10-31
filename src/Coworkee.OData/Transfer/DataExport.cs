using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Data;
using Coworkee.OData.Computed;
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
/// The rows of a set as its list shows them ($filter, $search, $orderby), with the chosen columns and their headers (<c>columns=Name:Product,Price</c>, see
/// <see cref="ExportColumns"/>), as Excel, CSV or JSON; <see cref="ODataComputedProperty.Exported"/> computed properties follow the entity's own columns.
/// <c>route=rt/odata</c> reads the names of that OData route (<see cref="ODataRouteProfile"/>), <c>fileName</c> names the file.
/// Larger exports than <see cref="CoworkeeODataOptions.BackgroundExportRows"/> run as a job.
/// </summary>
public static class DataExport
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int ProgressRows = 500;

    public static ExportFileFormat Format(string? format) =>
        Enum.TryParse<ExportFileFormat>(format, ignoreCase: true, out var parsed) ? parsed : ExportFileFormat.Xlsx;

    /// <summary>The rows the query string selects; null when there are more than <paramref name="maxRows"/>.</summary>
    internal static async Task<ExportFile?> WriteAsync<TEntity>(
        IServiceProvider services, string entitySet, QueryString query, int? maxRows, IProgress<DataExportProgress>? progress, CancellationToken cancellationToken)
        where TEntity : class
    {
        progress?.Report(new DataExportProgress(DataExportProgress.Counting, 0));
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.QueryString = query;
        var route = Route(services, http.Request.Query["route"]);
        http.ODataFeature().RoutePrefix = route.Prefix;
        var model = services.GetRequiredService<ODataModels>().Of(route);
        if (ODataEntityAccess.HasAliases(http.Request.Query))
        {
            throw new Microsoft.OData.ODataException("Parameter aliases (@name) are not supported.");
        }

        var options = http.Request.ODataQueryOptions<TEntity>(model);
        ODataLimits.Check(options, services);
        var denied = await ODataEntityAccess.DeniedPropertiesAsync(services, typeof(TEntity), cancellationToken);
        var deniedByType = await ODataEntityAccess.DeniedByTypeAsync(services, cancellationToken);
        if (ODataQueryGuard.Denies(options, deniedByType))
        {
            throw new UnauthorizedAccessException("The export filters or sorts on a property the user may not read.");
        }

        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        var searched = await ODataEntityAccess.SearchAsync(services, await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken), options.Search?.RawValue,
            deniedByType, scope, cancellationToken);
        var rows = await scope.ApplyAsync(searched.ApplyODataFilter(options).ApplyODataOrderBy(options), cancellationToken);
        var limit = services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.MaxExportRows;
        var total = maxRows is not null || progress is not null ? await rows.CountAsync(cancellationToken) : 0;
        if (maxRows is { } max && total > max)
        {
            return null;
        }

        var computed = await ExportedAsync<TEntity>(services, cancellationToken);
        var list = new List<ExportRow>();
        var expected = Math.Max(1, Math.Min(total, limit));
        progress?.Report(new DataExportProgress(DataExportProgress.Reading, 0) { Rows = 0, Total = Math.Min(total, limit) });
        await foreach (var row in Rows(rows, computed).Take(limit).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            list.Add(row);
            if (progress is not null && list.Count % ProgressRows == 0)
            {
                progress.Report(new DataExportProgress(DataExportProgress.Reading, Math.Min(80, list.Count * 80 / expected)) { Rows = list.Count, Total = Math.Min(total, limit) });
            }
        }

        progress?.Report(new DataExportProgress(DataExportProgress.Writing, 80) { Rows = list.Count, Total = list.Count });
        var wanted = ExportColumns.Parse(http.Request.Query["columns"]);
        var edm = (IEdmStructuredType)model.FindDeclaredType(typeof(TEntity).FullName);
        List<Column> all =
        [
            .. Columns<TEntity>(model, denied).Select(p => new Column(p.Name, p.Name, row => p.Property.GetValue(row.Entity))),
            .. computed.Select((c, i) => new Column(Named(c.Name), Named(c.Name), row => row.Computed[i])),
        ];
        var columns = wanted.Count == 0
            ? all
            : [.. wanted.Select(w => all.FirstOrDefault(c => string.Equals(c.Name, w.Field, StringComparison.OrdinalIgnoreCase)) is { } c ? c with { Header = w.Header ?? c.Name } : null).OfType<Column>()];
        var name = DataExports.FileName(null, entitySet, query);

        // a computed column in the route's case (rt/odata: margin)
        string Named(string computedName) =>
            edm.StructuralProperties().FirstOrDefault(p => string.Equals(p.Name, computedName, StringComparison.OrdinalIgnoreCase))?.Name ?? computedName;
        return Format(http.Request.Query["format"]) switch
        {
            ExportFileFormat.Csv => new ExportFile(Csv(columns, list), "text/csv", name),
            ExportFileFormat.Json => new ExportFile(Json(columns, list), "application/json", name),
            _ => new ExportFile(Xlsx(entitySet, columns, list), XlsxContentType, name),
        };
    }

    /// <summary>The OData route whose names the export uses (<c>route=rt/odata</c>); the default route without one.</summary>
    internal static ODataRouteProfile Route(IServiceProvider services, string? prefix)
    {
        var routes = services.GetRequiredService<ODataRoutes>();
        return string.IsNullOrWhiteSpace(prefix) ? routes.Default : routes.Find(prefix) ?? throw new Microsoft.OData.ODataException($"There is no OData route {prefix}.");
    }

    /// <summary>The exportable properties of the EDM type under their names in the model, without <paramref name="denied"/> ones (CLR names).</summary>
    internal static List<(string Name, PropertyInfo Property)> Columns<TEntity>(IEdmModel model, IReadOnlySet<string> denied)
    {
        var type = (IEdmStructuredType)model.FindDeclaredType(typeof(TEntity).FullName);
        return
        [
            .. type.StructuralProperties()
                .Where(p => p.Type.IsPrimitive() || p.Type.IsEnum())
                .Select(p => (p.Name, Property: typeof(TEntity).GetProperty(ODataNames.Clr(model, p))))
                .Where(p => p.Property is not null && !denied.Contains(p.Property.Name))
                .Select(p => (p.Name, p.Property!)),
        ];
    }

    private static async Task<List<ODataComputedProperty>> ExportedAsync<TEntity>(IServiceProvider services, CancellationToken cancellationToken)
    {
        var exported = new List<ODataComputedProperty>();
        foreach (var property in services.GetRequiredService<ODataComputedProperties>().Of(typeof(TEntity)).Where(p => p.Exported))
        {
            if (property.Permission is null || await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(property.Permission, cancellationToken))
            {
                exported.Add(property);
            }
        }

        return exported;
    }

    /// <summary>Each row with the values of the computed columns, evaluated by the database in the same query.</summary>
    private static IQueryable<ExportRow> Rows<TEntity>(IQueryable<TEntity> rows, List<ODataComputedProperty> computed)
    {
        var row = Expression.Parameter(typeof(TEntity));
        var values = Expression.NewArrayInit(typeof(object), computed.Select(c => Expression.Convert(c.Bind(row), typeof(object))));
        return rows.Select(Expression.Lambda<Func<TEntity, ExportRow>>(Expression.New(typeof(ExportRow).GetConstructors()[0], row, values), row));
    }

    private sealed record ExportRow(object Entity, object?[] Computed);

    /// <param name="Name">The field in the route's names; the key of a JSON row.</param>
    /// <param name="Header">The header of a CSV or Excel column.</param>
    private sealed record Column(string Name, string Header, Func<ExportRow, object?> Value);

    private static byte[] Csv(List<Column> columns, List<ExportRow> rows) =>
        CsvFile.Write([columns.Select(c => c.Header), .. rows.Select(row => columns.Select(c => Text(c.Value(row))))]);

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("o", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static byte[] Json(List<Column> columns, List<ExportRow> rows) =>
        JsonSerializer.SerializeToUtf8Bytes(
            rows.Select(row => columns.ToDictionary(c => c.Name, c => c.Value(row))),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

    private static byte[] Xlsx(string entitySet, List<Column> columns, List<ExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(entitySet.Length > 31 ? entitySet[..31] : entitySet);
        for (var column = 0; column < columns.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = columns[column].Header;
            for (var row = 0; row < rows.Count; row++)
            {
                sheet.Cell(row + 2, column + 1).Value = Cell(columns[column].Value(rows[row]));
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
