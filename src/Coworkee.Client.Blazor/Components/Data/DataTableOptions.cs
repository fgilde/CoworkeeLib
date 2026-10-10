namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Defaults for every <see cref="CoworkeeDataTable{T}"/> of the app.</summary>
public sealed class DataTableOptions
{
    private readonly Dictionary<Type, string[]> _operators = [];

    /// <summary>Filter icons in the column headers, unless a table sets <c>ColumnFilters</c> itself.</summary>
    public bool ColumnFilters { get; set; } = true;

    /// <summary>
    /// The operators column filters offer for <typeparamref name="TValue"/>: MudBlazor's <c>FilterOperator</c> names and <see cref="Blazor.Data.DataTableFilters.Between"/>.
    /// No operators turn filtering off for that type; <c>typeof(Enum)</c> stands for all enums.
    /// </summary>
    public DataTableOptions FilterOperators<TValue>(params string[] operators)
    {
        _operators[typeof(TValue)] = operators;
        return this;
    }

    internal IReadOnlyList<string> OperatorsFor(Type type) =>
        _operators.GetValueOrDefault(type) ?? (type.IsEnum ? _operators.GetValueOrDefault(typeof(Enum)) : null) ?? Blazor.Data.DataTableFilters.DefaultOperators(type);
}
