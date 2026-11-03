namespace Coworkee.Client.Blazor.Data;

/// <summary>One condition on a property: its OData path (<c>Brand/Name</c>), the property type without <c>Nullable</c>, a MudBlazor <c>FilterOperator</c> and the value.</summary>
public sealed record ColumnCondition(string Path, Type ValueType, string Operator, object? Value);
