namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>How a <see cref="CoworkeeDataTable{T}"/> shows more rows than fit on a page.</summary>
public enum DataTablePageMode
{
    /// <summary>The setting <c>DataTable.DefaultPageMode</c> (global, the user may override it); follows it when it changes.</summary>
    FromSettings,

    /// <summary>Pages with a pager below the rows.</summary>
    Pager,

    /// <summary>One scrolling list that loads the next rows while scrolling and renders only the visible ones.</summary>
    EndlessScroll,
}
