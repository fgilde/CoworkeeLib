using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Shows, hides and orders the columns of a data grid.</summary>
public partial class DataTableColumnChooser<T>
{
    private string? _filter;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public MudDataGrid<T> Grid { get; set; } = null!;

    /// <summary>After a column was shown, hidden or moved.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    private IReadOnlyList<Column<T>> Columns => DataGridLayout<T>.Named(Grid);

    private bool Matches(Column<T> column) =>
        string.IsNullOrWhiteSpace(_filter) || DataGridLayout<T>.Label(column).Contains(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private async Task SetVisibleAsync(Column<T> column, bool visible)
    {
        await DataGridLayout<T>.SetHiddenAsync(column, !visible);
        await Changed.InvokeAsync();
    }

    private async Task ShowAllAsync(bool visible)
    {
        foreach (var column in Columns)
        {
            await DataGridLayout<T>.SetHiddenAsync(column, !visible);
        }

        await Changed.InvokeAsync();
    }

    internal async Task DroppedAsync(MudItemDropInfo<Column<T>> drop)
    {
        var order = Columns.Select(c => DataGridLayout<T>.Key(c)!).ToList();
        var key = DataGridLayout<T>.Key(drop.Item!)!;
        order.Remove(key);
        order.Insert(Math.Clamp(drop.IndexInZone, 0, order.Count), key);
        DataGridLayout<T>.Order(Grid, order);
        await Changed.InvokeAsync();
    }

    private static string? Key(Column<T> column) => DataGridLayout<T>.Key(column);

    private static string Label(Column<T> column) => DataGridLayout<T>.Label(column);

    private static bool IsHidden(Column<T> column) => DataGridLayout<T>.IsHidden(column);
}
