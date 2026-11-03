namespace Coworkee.Client.Blazor.Data;

/// <summary>A column filter as <see cref="DataTableState"/> keeps it: the column's key, a MudBlazor <c>FilterOperator</c> and the value as invariant text.</summary>
public sealed record DataTableFilter(string Column, string Operator, string? Value);
