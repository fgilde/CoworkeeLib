using System.Reflection;
using Coworkee.Client.Blazor.Data;
using MudBlazor;
using MudBlazor.State;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Reads and restores what the user changed on a <see cref="MudDataGrid{T}"/>: column order, visibility, widths, grouping, sorting and column filters.</summary>
// ponytail: MudBlazor 9 keeps hidden, grouping and width state internal, so it is reached by reflection; DataTableLayoutTests fail when MudBlazor renames it, switch to public API once MudBlazor has one
internal static class DataGridLayout<T>
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly PropertyInfo HiddenState = typeof(Column<T>).GetProperty("HiddenState", Internal)!;
    private static readonly PropertyInfo GroupingState = typeof(Column<T>).GetProperty("GroupingState", Internal)!;
    private static readonly FieldInfo GroupOrderState = typeof(Column<T>).GetField("_groupByOrderState", Internal)!;
    private static readonly PropertyInfo HeaderCell = typeof(Column<T>).GetProperty("HeaderCell", Internal)!;
    private static readonly PropertyInfo Width = typeof(HeaderCell<T>).GetProperty(nameof(HeaderCell<T>.Width))!;

    /// <summary>Property columns are named by their property, template columns by their title; select, expand and action columns have no name.</summary>
    public static string? Key(Column<T> column) =>
        IsProperty(column) ? column.PropertyName : string.IsNullOrEmpty(column.Title) ? null : column.Title;

    /// <summary>A <see cref="PropertyColumn{T, TProperty}"/>, or a column that filters, sorts and groups on a property of the set like one (the workflow column).</summary>
    public static bool IsProperty(Column<T> column) =>
        column is IPropertyLikeColumn || column.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(PropertyColumn<,>);

    /// <summary>The property type of a property column, without <c>Nullable</c>.</summary>
    public static Type? ValueType(Column<T> column) =>
        column is IPropertyLikeColumn like ? like.ValueType
        : IsProperty(column) ? column.GetType().GetGenericArguments()[1] is var type && Nullable.GetUnderlyingType(type) is { } inner ? inner : type : null;

    /// <summary>The column filters of the grid; facet markers and filters without a property are left out.</summary>
    public static IEnumerable<IFilterDefinition<T>> Filters(MudDataGrid<T> grid) =>
        grid.FilterDefinitions.Where(d => d.Operator != DataTableFilters.FacetMarker && d.Column is { } column && IsProperty(column));

    public static IEnumerable<ColumnCondition> Conditions(MudDataGrid<T> grid) =>
        Filters(grid).Select(d => new ColumnCondition(d.Column!.PropertyName!.Replace('.', '/'), ValueType(d.Column)!, d.Operator ?? string.Empty, d.Value));

    public static string Label(Column<T> column) => string.IsNullOrEmpty(column.Title) ? column.PropertyName ?? string.Empty : column.Title;

    public static IReadOnlyList<Column<T>> Named(MudDataGrid<T> grid) => grid.RenderedColumns.Where(c => Key(c) is not null).ToList();

    public static bool IsHidden(Column<T> column) => State<bool>(HiddenState.GetValue(column)).Value;

    public static IReadOnlyList<Column<T>> Grouped(MudDataGrid<T> grid) =>
        grid.RenderedColumns.Where(c => State<bool>(GroupingState.GetValue(c)).Value).OrderBy(c => State<int>(GroupOrderState.GetValue(c)).Value).ToList();

    public static DataTableState Capture(MudDataGrid<T> grid, DataTableState filters)
    {
        var named = Named(grid);
        var widths = named.Select(c => (Key: Key(c)!, Width: HeaderCell.GetValue(c) is HeaderCell<T> header ? header.Width : null))
            .Where(w => w.Width is not null).ToDictionary(w => w.Key, w => w.Width!.Value);
        var groups = Grouped(grid).Select(c => Key(c)!).ToList();
        var sorts = grid.SortDefinitions.Values.OrderBy(s => s.Index).Select(s => new DataTableSort(s.SortBy, s.Descending)).ToList();
        return filters with
        {
            HiddenColumns = named.Where(IsHidden).Select(c => Key(c)!).ToList(),
            ColumnOrder = named.Select(c => Key(c)!).ToList(),
            GroupBy = groups.Count == 0 ? null : groups,
            Sort = sorts.Count == 0 ? null : sorts,
            Widths = widths.Count == 0 ? null : widths,
            ColumnFilters = Filters(grid).Select(d => new DataTableFilter(Key(d.Column!)!, d.Operator ?? string.Empty, DataTableFilters.Format(d.Value))).ToList() is { Count: > 0 } columnFilters
                ? columnFilters : null,
        };
    }

    /// <summary>Applies everything but sorting and page size, which reload and are left to the table.</summary>
    public static async Task ApplyAsync(MudDataGrid<T> grid, DataTableState state, IReadOnlyList<string> declaredOrder)
    {
        Order(grid, state.ColumnOrder is { Count: > 0 } order ? order : declaredOrder);
        foreach (var column in Named(grid))
        {
            var hidden = state.HiddenColumns.Contains(Key(column)!) || state.HiddenColumns.Contains(column.Title ?? string.Empty);
            await State<bool>(HiddenState.GetValue(column)).SetValueAsync(hidden);
            if (HeaderCell.GetValue(column) is HeaderCell<T> header)
            {
                Width.SetValue(header, state.Widths?.GetValueOrDefault(Key(column)!) is > 0 and var width ? width : null);
            }
        }

        await GroupAsync(grid, state.GroupBy ?? []);
        RestoreFilters(grid, state.ColumnFilters ?? []);
    }

    private static void RestoreFilters(MudDataGrid<T> grid, IReadOnlyList<DataTableFilter> filters)
    {
        grid.FilterDefinitions.RemoveAll(d => d.Operator != DataTableFilters.FacetMarker);
        foreach (var filter in filters)
        {
            if (Named(grid).FirstOrDefault(c => IsProperty(c) && Key(c) == filter.Column) is { } column)
            {
                grid.FilterDefinitions.Add(Definition(column, filter.Operator, DataTableFilters.Parse(filter.Value, ValueType(column)!)));
            }
        }
    }

    public static FilterDefinition<T> Definition(Column<T> column, string @operator, object? value) =>
        new() { Id = Guid.NewGuid(), Column = column, Title = column.Title, Operator = @operator, Value = value };

    public static async Task SetHiddenAsync(Column<T> column, bool hidden) => await State<bool>(HiddenState.GetValue(column)).SetValueAsync(hidden);

    public static async Task GroupAsync(MudDataGrid<T> grid, IReadOnlyList<string> keys)
    {
        foreach (var column in grid.RenderedColumns)
        {
            var index = IsProperty(column) ? keys.ToList().IndexOf(Key(column)!) : -1;
            await State<bool>(GroupingState.GetValue(column)).SetValueAsync(index >= 0);
            await State<int>(GroupOrderState.GetValue(column)).SetValueAsync(Math.Max(index, 0));
        }

        // also renders the grid and refreshes the header drop zones
        grid.GroupItems();
    }

    /// <summary>Moves the named columns into the given order; unnamed columns (select, actions) keep their places.</summary>
    public static void Order(MudDataGrid<T> grid, IReadOnlyList<string> order)
    {
        var columns = grid.RenderedColumns;
        var slots = Enumerable.Range(0, columns.Count).Where(i => Key(columns[i]) is not null).ToList();
        var sorted = slots.Select(i => columns[i])
            .OrderBy(c => order.ToList().IndexOf(Key(c)!) is >= 0 and var position ? position : int.MaxValue)
            .ToList();
        for (var i = 0; i < slots.Count; i++)
        {
            columns[slots[i]] = sorted[i];
        }

        grid.GroupItems();
    }

    public static async Task SortAsync(MudDataGrid<T> grid, IReadOnlyList<DataTableSort> sorts)
    {
        var current = grid.SortDefinitions.Values.OrderBy(s => s.Index).Select(s => new DataTableSort(s.SortBy, s.Descending));
        if (current.SequenceEqual(sorts))
        {
            return;
        }

        foreach (var field in grid.SortDefinitions.Keys.ToList())
        {
            await grid.RemoveSortAsync(field);
        }

        foreach (var sort in sorts)
        {
            if (grid.RenderedColumns.FirstOrDefault(c => c.PropertyName == sort.Column) is { } column)
            {
                await grid.ExtendSortAsync(sort.Column, sort.Descending ? SortDirection.Descending : SortDirection.Ascending, column.SortBy);
            }
        }
    }

    private static ParameterState<TValue> State<TValue>(object? state) => (ParameterState<TValue>)state!;
}

/// <summary>A column without a property of the row type that the table treats like a property column of <see cref="ValueType"/>, named by its PropertyName.</summary>
internal interface IPropertyLikeColumn
{
    Type ValueType { get; }
}
