using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Feeds a data table from an entity set: maps the column properties back to OData paths for sorting.</summary>
public sealed partial class DynamicRowSource(IODataClient odata, string entitySet, IReadOnlyList<string> paths) : ITableDataSource<DynamicRow>
{
    public async Task<ODataPage<DynamicRow>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default)
    {
        var page = await odata.QueryAsync<JsonElement>(entitySet, query with
        {
            OrderBy = query.OrderBy is { } order ? ColumnName().Replace(order, m => paths[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]) : null,
            Select = RowValues.Select(paths),
            Expand = RowValues.Expand(paths),
        }, cancellationToken);
        return new ODataPage<DynamicRow>([.. page.Items.Select(i => DynamicRow.From(i, paths))], page.Count, page.Facets);
    }

    [GeneratedRegex(@"\bC(\d{1,2})\b")]
    private static partial Regex ColumnName();
}
