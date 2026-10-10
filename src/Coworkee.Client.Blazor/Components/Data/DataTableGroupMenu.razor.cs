using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Chooses and orders the columns a data grid groups by.</summary>
public partial class DataTableGroupMenu<T>
{
    private const string GroupedZone = "grouped";

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public MudDataGrid<T> Grid { get; set; } = null!;

    /// <summary>With the new grouping columns, outermost first.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> GroupingChanged { get; set; }

    private IReadOnlyList<string> Grouped => DataGridLayout<T>.Grouped(Grid).Select(c => DataGridLayout<T>.Key(c)!).ToList();

    private IReadOnlyList<Column<T>> Ordered =>
        DataGridLayout<T>.Named(Grid).Where(DataGridLayout<T>.IsProperty).OrderBy(c => IsGrouped(c) ? Position(c) : int.MaxValue).ToList();

    private bool IsGrouped(Column<T> column) => Position(column) >= 0;

    private int Position(Column<T> column) => Grouped.ToList().IndexOf(DataGridLayout<T>.Key(column)!);

    private Task ToggleAsync(Column<T> column)
    {
        var key = DataGridLayout<T>.Key(column)!;
        return GroupAsync(IsGrouped(column) ? Grouped.Where(k => k != key).ToList() : [.. Grouped, key]);
    }

    internal Task DroppedAsync(MudItemDropInfo<Column<T>> drop)
    {
        var key = DataGridLayout<T>.Key(drop.Item!)!;
        var grouped = Grouped.Where(k => k != key).ToList();
        if (drop.DropzoneIdentifier == GroupedZone)
        {
            grouped.Insert(Math.Clamp(drop.IndexInZone, 0, grouped.Count), key);
        }

        return GroupAsync(grouped);
    }

    private async Task GroupAsync(IReadOnlyList<string> keys)
    {
        await DataGridLayout<T>.GroupAsync(Grid, keys);
        await GroupingChanged.InvokeAsync(keys);
    }

    private static string? Key(Column<T> column) => DataGridLayout<T>.Key(column);

    private static string Label(Column<T> column) => DataGridLayout<T>.Label(column);
}
