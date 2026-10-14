using Coworkee.Client.Blazor.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A component every <see cref="CoworkeeDataTable{T}"/> over an OData set shows in its toolbar, registered in DI by a package
/// (client entities add their switch this way). The component gets the table as parameter <c>Table</c>.
/// </summary>
public sealed record DataTableToolbarItem(Type Component);

/// <summary>What a <see cref="DataTableToolbarItem"/> knows of its table.</summary>
public interface IDataTable
{
    string EntitySet { get; }

    string? Expand { get; }

    /// <summary>The query of the rows shown now: page filter, facets, column filters, search and order, the first page with facets.</summary>
    ODataQuery CurrentQuery { get; }

    Task ReloadAsync();
}
