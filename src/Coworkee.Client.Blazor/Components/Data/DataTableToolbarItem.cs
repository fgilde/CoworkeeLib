namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A component every <see cref="CoworkeeDataTable{T}"/> over an OData set shows in its toolbar, registered in DI by a package
/// (client entities add their switch this way). The component gets the table as parameter <c>Table</c>.
/// </summary>
public sealed record DataTableToolbarItem(Type Component);
