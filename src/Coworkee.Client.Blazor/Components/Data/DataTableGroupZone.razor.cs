using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The row above the column headers that shows the grouping; column headers dropped on it group the rows.</summary>
/// <remarks>Rendered inside the grid's header drop container, so header drags reach it and it renders with every grid render.</remarks>
public partial class DataTableGroupZone<T>
{
    internal const string ZoneId = "__cw_group__";

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public MudDataGrid<T> Grid { get; set; } = null!;

    /// <summary>Column headers can be dragged onto the zone (the grid reorders columns by drag and drop).</summary>
    [Parameter] public bool Droppable { get; set; } = true;

    [Parameter] public EventCallback<IReadOnlyList<string>> GroupingChanged { get; set; }

    private IReadOnlyList<Column<T>> Grouped => DataGridLayout<T>.Grouped(Grid);

    private bool CanGroup(Column<T> column) => DataGridLayout<T>.IsProperty(column) && !Grouped.Contains(column);

    // the grid handles header drops itself and only marks the dropped column with the zone's identifier
    protected override Task OnParametersSetAsync() =>
        Grid.RenderedColumns.FirstOrDefault(c => c.Identifier == ZoneId) is { } dropped ? GroupDroppedAsync(dropped) : Task.CompletedTask;

    private Task GroupDroppedAsync(Column<T> column)
    {
#pragma warning disable BL0005 // reset the mark the grid set on drop
        column.Identifier = null;
#pragma warning restore BL0005
        return CanGroup(column) ? GroupAsync([.. Keys, DataGridLayout<T>.Key(column)!]) : Task.CompletedTask;
    }

    private Task UngroupAsync(Column<T> column) => GroupAsync(Keys.Where(k => k != DataGridLayout<T>.Key(column)).ToList());

    private IReadOnlyList<string> Keys => Grouped.Select(c => DataGridLayout<T>.Key(c)!).ToList();

    private async Task GroupAsync(IReadOnlyList<string> keys)
    {
        await DataGridLayout<T>.GroupAsync(Grid, keys);
        await GroupingChanged.InvokeAsync(keys);
    }

    private static string? Key(Column<T> column) => DataGridLayout<T>.Key(column);

    private static string Label(Column<T> column) => DataGridLayout<T>.Label(column);
}
