using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Op = MudBlazor.FilterOperator;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    /// <summary>
    /// A filter in the header of every property column; defaults to <see cref="DataTableOptions.ColumnFilters"/>. A column turns it off or on with
    /// MudBlazor's <c>Filterable</c>, narrows it with <c>FilterOperators</c> and brings its own editor with <c>FilterTemplate</c>.
    /// </summary>
    [Parameter] public bool? ColumnFilters { get; set; }

    private bool Filterable => ColumnFilters ?? Options.DataTable.ColumnFilters;

    private IReadOnlyList<FilterChip> FilterChips => _grid is null ? [] :
        [
            .. DataGridLayout<T>.Filters(_grid).GroupBy(d => d.Column!).Select(g => new FilterChip(DataGridLayout<T>.Key(g.Key)!, DataGridLayout<T>.Label(g.Key), Describe([.. g]))),
            .. !HasAdvanced ? [] : new[] { new FilterChip(AdvancedKey, L["Advanced filter"], QueryDescriber.Describe(_advanced, Fields, L10n)) },
        ];

    private string? ColumnFilter => _grid is null ? null : DataTableFilters.ToOData(DataGridLayout<T>.Conditions(_grid));

    private RenderFragment<FilterContext<T>> FilterEditor => context => builder =>
    {
        var column = context.FilterDefinition!.Column!;
        builder.OpenComponent<DataTableColumnFilter<T>>(0);
        builder.AddComponentParameter(1, nameof(DataTableColumnFilter<T>.Context), context);
        builder.AddComponentParameter(2, nameof(DataTableColumnFilter<T>.Facet), FacetOf(column));
        builder.AddComponentParameter(3, nameof(DataTableColumnFilter<T>.Selection), _selection);
        builder.AddComponentParameter(4, nameof(DataTableColumnFilter<T>.Operators), OperatorsOf(column));
        builder.CloseComponent();
    };

    // template columns have no property to filter; property columns get the editor unless they bring their own
    private void FitFilter(Column<T> column)
    {
#pragma warning disable BL0005 // only where the page left the parameter unset
        if (!DataGridLayout<T>.IsProperty(column) || OperatorsOf(column).Count == 0)
        {
            column.Filterable ??= false;
        }

        column.FilterTemplate ??= FilterEditor;
#pragma warning restore BL0005
    }

    private IReadOnlyList<string> OperatorsOf(Column<T> column) =>
        column.FilterOperators.Count > 0 ? [.. column.FilterOperators] : DataGridLayout<T>.ValueType(column) is { } type ? Options.DataTable.OperatorsFor(type) : [];

    /// <summary>The facet on the column's property, or on the key of its navigation property (<c>Brand.Name</c> uses a facet on <c>BrandId</c>).</summary>
    private FacetGroupDto? FacetOf(Column<T> column)
    {
        if (column.PropertyName is not { } property)
        {
            return null;
        }

        var root = property.Split('.')[0];
        return _facets.FirstOrDefault(f => f.Field.Equals(property, StringComparison.OrdinalIgnoreCase)
            || (property.Contains('.') && (f.Field.Equals(root, StringComparison.OrdinalIgnoreCase) || f.Field.Equals(root + "Id", StringComparison.OrdinalIgnoreCase))));
    }

    // a column whose facet has a choice shows its filter icon as active
    private void MarkFacetColumns()
    {
        _grid!.FilterDefinitions.RemoveAll(d => d.Operator == DataTableFilters.FacetMarker);
        foreach (var column in DataGridLayout<T>.Named(_grid))
        {
            if (FacetOf(column) is { } facet && _selection.All.Any(f => f.GroupKey == facet.Key))
            {
                _grid.FilterDefinitions.Add(DataGridLayout<T>.Definition(column, DataTableFilters.FacetMarker, facet.Key));
            }
        }
    }

    private string Describe(IReadOnlyList<IFilterDefinition<T>> filters) =>
        filters.All(f => f.Operator is Op.Enum.Is or Op.String.Equal or Op.Number.Equal)
            ? string.Join(", ", filters.Select(f => Show(f.Value)))
            : string.Join(", ", filters.Select(f => $"{L[f.Operator ?? string.Empty]} {Show(f.Value)}".Trim()));

    private string Show(object? value) => value switch
    {
        null => string.Empty,
        bool flag => L[flag ? "Yes" : "No"],
        DateTime date => date.ToShortDateString(),
        DateTimeOffset offset => offset.LocalDateTime.ToShortDateString(),
        DateOnly date => date.ToShortDateString(),
        Enum enumValue => L[EnumLabels.Of(enumValue)],
        _ => value.ToString() ?? string.Empty,
    };

    private void ColumnFiltersChanged(IReadOnlyCollection<IFilterDefinition<T>> filters)
    {
        WriteUrl();
        StateHasChanged();
    }

    private Task RemoveFilterAsync(FilterChip chip)
    {
        if (chip.Key == AdvancedKey)
        {
            return AdvancedChangedAsync(new QueryGroup());
        }

        _grid!.FilterDefinitions.RemoveAll(d => d.Operator != DataTableFilters.FacetMarker && d.Column is { } c && DataGridLayout<T>.Key(c) == chip.Key);
        WriteUrl();
        return ReloadAsync();
    }

    private void ClearFilters()
    {
        _grid?.FilterDefinitions.RemoveAll(d => d.Operator != DataTableFilters.FacetMarker);
        (_advanced, _lastAdvanced) = (new QueryGroup(), null);
        // reloads and writes the address once
        _selection.Clear();
    }
}
