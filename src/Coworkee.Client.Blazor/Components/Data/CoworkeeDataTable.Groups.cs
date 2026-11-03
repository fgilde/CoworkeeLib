using System.Globalization;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Grouped, the table lists the groups of the whole result with their counts (<see cref="IODataClient.GroupAsync{T}"/>) and loads the rows of a group
/// when it opens (<see cref="DataTableGroupList{T}"/>, <see cref="DataTableGroupRows{T}"/>). A data source or a client that cannot group keeps the
/// grid's grouping of the loaded page.
/// </summary>
public partial class CoworkeeDataTable<T>
{
    private readonly HashSet<string> _openGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<T>> _groupSelections = new(StringComparer.Ordinal);
    private bool _groupsUnsupported;
    private string? _groupedBy;
    private long _groupTotal;
    private int _groupVersion;

    /// <summary>Grouped, with groups over the whole result rather than the grid's grouping of a page.</summary>
    private bool GroupsTheResult => IsGrouped && DataSource is null && !_groupsUnsupported;

    /// <summary>The grouping columns in their order, with the direction their groups are listed in.</summary>
    internal IReadOnlyList<DataTableGroupLevel> GroupLevels => _grid is null ? [] : [.. DataGridLayout<T>.Grouped(_grid).Select(c => new DataTableGroupLevel(
        c.PropertyName!, DataGridLayout<T>.Label(c), _grid.SortDefinitions.TryGetValue(c.PropertyName!, out var sort) && sort.Descending))];

    /// <summary>Changes with every load of the grouped table: the open groups load again.</summary>
    internal int GroupVersion => _groupVersion;

    internal bool IsGroupOpen(string conditions) => _openGroups.Contains(conditions);

    internal void ToggleGroup(string conditions)
    {
        if (!_openGroups.Remove(conditions))
        {
            _openGroups.Add(conditions);
        }
        else
        {
            // a closed group's rows are no longer selected
            foreach (var key in _groupSelections.Keys.Where(k => k.StartsWith(conditions, StringComparison.Ordinal)).ToList())
            {
                _groupSelections.Remove(key);
            }

            _ = SelectionChangedAsync([.. _groupSelections.Values.SelectMany(s => s)]);
        }

        StateHasChanged();
    }

    /// <summary>The table's filter and the conditions of the open groups above.</summary>
    internal string? GroupFilter(string? conditions) => ODataFilter.And(CurrentFilter, conditions);

    // in parentheses, so the conditions of a group start with those of the group above it (ToggleGroup)
    internal static string GroupCondition(string? conditions, string path, object? value)
    {
        var property = path.Replace('.', '/');
        return ODataFilter.And(conditions, value is null ? $"({property} eq null)" : $"({property} eq {DataTableFilters.Literal(value)})")!;
    }

    /// <summary>The groups of one level in value order; null when the client cannot group (the table then groups the loaded page).</summary>
    internal async Task<IReadOnlyList<ODataGroup>?> GroupsAsync(string? conditions, DataTableGroupLevel level, CancellationToken cancellationToken)
    {
        try
        {
            var groups = await OData.GroupAsync<T>(EntitySet, GroupFilter(conditions), [level.Path], cancellationToken);
            var ordered = groups.OrderBy(g => g.Values[0], Comparer<object?>.Default);
            return [.. level.Descending ? ordered.Reverse() : ordered];
        }
        catch (NotSupportedException)
        {
            _groupsUnsupported = true;
            await ReloadAsync();
            StateHasChanged();
            return null;
        }
    }

    /// <summary>The rows of an open group, sorted like the table apart from the grouping columns.</summary>
    internal async Task<ODataPage<T>> GroupRowsAsync(string? conditions, int skip, int top, bool count, CancellationToken cancellationToken)
    {
        var groups = GroupLevels.Select(l => l.Path).ToHashSet(StringComparer.Ordinal);
        var sorts = _grid is null ? [] : _grid.SortDefinitions.Values.Where(s => !groups.Contains(s.SortBy)).OrderBy(s => s.Index)
            .Select(s => $"{s.SortBy.Replace('.', '/')} {(s.Descending ? "desc" : "asc")}").ToList();
        var page = await QueryAsync(new ODataQuery
        {
            Filter = GroupFilter(conditions), OrderBy = sorts.Count == 0 ? null : string.Join(",", sorts), Expand = Expand, Top = top, Skip = skip, Count = count,
        }, cancellationToken);
        await OnLoaded.InvokeAsync(page.Items);
        return page;
    }

    /// <summary>What a group's rows take over from the table: column order, hidden columns and widths.</summary>
    internal DataTableState GroupRowsLayout => CurrentState with { GroupBy = null, ColumnFilters = null, Sort = null };

    internal Task GroupSelectionChangedAsync(string conditions, HashSet<T> selected)
    {
        _groupSelections[conditions] = selected;
        return SelectionChangedAsync([.. _groupSelections.Values.SelectMany(s => s)]);
    }

    // the grid gets no rows; it shows the groups instead (NoRecordsContent) and the count of the whole result below them. The count query also starts the
    // client entity copy with the table's expand, which the group counts use
    private async Task<GridData<T>> LoadGroupsAsync(GridState<T> state, CancellationToken cancellationToken)
    {
        _orderBy = OrderBy(state.SortDefinitions);
        var filter = CurrentFilter;
        LoadFacets(filter, Facets ? _selection.Context(BaseFilter, _facets) : null);
        var groupedBy = string.Join(",", GroupLevels.Select(l => l.Path));
        if (groupedBy != _groupedBy)
        {
            _groupedBy = groupedBy;
            _openGroups.Clear();
            _groupSelections.Clear();
        }

        var page = await QueryAsync(new ODataQuery { Filter = filter, Expand = Expand, Top = 0, Count = true }, cancellationToken);
        _groupTotal = page.Count ?? 0;
        _groupVersion++;
        return new GridData<T> { Items = [], TotalItems = 0 };
    }

    /// <summary>The value as the facet of the column reads it (an enum's translated text, a choice's name); null without such a facet.</summary>
    internal string? GroupLabel(string path, object value)
    {
        if (_grid is null || DataGridLayout<T>.Named(_grid).FirstOrDefault(c => c.PropertyName == path) is not { } column
            || FacetOf(column) is not { } facet || !facet.Field.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return facet.Options.FirstOrDefault(o => o.Value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(o.Value.GetString(), text, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => o.Value.TryGetDecimal(out var number) && (value is Enum ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : decimal.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : decimal.MinValue) == number,
            JsonValueKind.True or JsonValueKind.False => value is bool flag && flag == o.Value.GetBoolean(),
            _ => false,
        }) is { } option ? FacetLabels.Of(L, facet, option) : null;
    }

    internal float GroupRowHeight => RowHeight;

    internal bool GroupRowsDense => Dense;

    internal bool GroupRowsStriped => Theme.StripedTables;

    internal bool GroupRowsEndless => ResolvedPageMode == DataTablePageMode.EndlessScroll;

    internal string? GroupRowClass(T item, int index) => RowClass(item, index);

    internal Task GroupRowClickedAsync(DataGridRowClickEventArgs<T> args) => RowClickedAsync(args);
}
