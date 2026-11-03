using Coworkee.Client.Blazor.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>What a <see cref="DataTableToolbarItem"/> knows of its table.</summary>
public interface IDataTable
{
    string EntitySet { get; }

    string? Expand { get; }

    /// <summary>The query of the rows shown now: page filter, facets, column filters, search and order, the first page with facets.</summary>
    ODataQuery CurrentQuery { get; }

    Task ReloadAsync();
}
