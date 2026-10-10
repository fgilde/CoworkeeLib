namespace Coworkee.Client.Blazor.Data;

/// <summary>A column filter as <see cref="DataTableState"/> keeps it: the column's key, a MudBlazor <c>FilterOperator</c> and the value as invariant text.</summary>
public sealed record DataTableFilter(string Column, string Operator, string? Value);

/// <summary>One condition on a property: its OData path (<c>Brand/Name</c>), the property type without <c>Nullable</c>, a MudBlazor <c>FilterOperator</c> and the value.</summary>
public sealed record ColumnCondition(string Path, Type ValueType, string Operator, object? Value);
