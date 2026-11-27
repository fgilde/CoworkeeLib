using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The row above the column headers that shows the grouping; column headers dropped on it group the rows.</summary>
/// <remarks>Rendered inside the grid's header drop container, so header drags reach it and it renders with every grid render.</remarks>
public partial class DataTableGroupZone<T> : IDisposable
{
    internal const string ZoneId = "__cw_group__";

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public MudDataGrid<T> Grid { get; set; } = null!;

    /// <summary>Column headers can be dragged onto the zone (the grid reorders columns by drag and drop).</summary>
    [Parameter] public bool Droppable { get; set; } = true;

    [Parameter] public EventCallback<IReadOnlyList<string>> GroupingChanged { get; set; }

    private IReadOnlyList<Column<T>> Grouped => DataGridLayout<T>.Grouped(Grid);

    private bool CanGroup(Column<T> column) => DataGridLayout<T>.IsProperty(column) && !Grouped.Contains(column);

    /// <summary>The grid's header drop container; the grid only handles drops on its own columns, so the zone listens for drops on itself.</summary>
    [CascadingParameter] private MudDropContainer<Column<T>>? Container { get; set; }

    private MudDropContainer<Column<T>>? _subscribed;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribed, Container))
            return;
        if (_subscribed is not null)
            _subscribed.TransactionEnded -= OnTransactionEnded;
        _subscribed = Container;
        if (_subscribed is not null)
            _subscribed.TransactionEnded += OnTransactionEnded;
    }

    private void OnTransactionEnded(object? sender, MudDragAndDropTransactionFinishedEventArgs<Column<T>> e)
    {
        if (e.Success && e.DestinationDropzoneIdentifier == ZoneId && e.Item is { } column && CanGroup(column))
            _ = InvokeAsync(() => GroupAsync([.. Keys, DataGridLayout<T>.Key(column)!]));
    }

    public void Dispose()
    {
        if (_subscribed is not null)
            _subscribed.TransactionEnded -= OnTransactionEnded;
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
