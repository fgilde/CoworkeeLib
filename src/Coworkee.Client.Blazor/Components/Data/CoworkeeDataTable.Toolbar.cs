using Coworkee.Client.Blazor.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T> : IDataTable
{
    private Dictionary<string, object>? _toolbarParameters;

    public ODataQuery CurrentQuery => new()
    {
        Filter = CurrentFilter, OrderBy = _orderBy, Expand = Expand, Top = _grid?.RowsPerPage ?? PageSize, Skip = 0, Facets = Facets,
        FacetContext = Facets ? _selection.Context(BaseFilter, _facets) : null,
    };

    private IEnumerable<DataTableToolbarItem> ToolbarItems => DataSource is null ? Services.GetServices<DataTableToolbarItem>() : [];

    private Dictionary<string, object> ToolbarParameters => _toolbarParameters ??= new() { ["Table"] = this };
}
