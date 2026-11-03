using System.Globalization;
using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The groups of one grouping column inside the groups opened above it, with their counts; an open group shows the next column's groups or its rows.</summary>
public partial class DataTableGroupList<T> : IDisposable
{
    private IReadOnlyList<ODataGroup>? _groups;
    private (int Version, string? Conditions)? _loaded;
    private CancellationTokenSource? _loading;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public CoworkeeDataTable<T> Table { get; set; } = null!;

    /// <summary>The conditions of the groups this list is inside of; null at the top.</summary>
    [Parameter] public string? Conditions { get; set; }

    [Parameter] public int Depth { get; set; }

    /// <summary>The table's load these groups belong to; a new one counts them again.</summary>
    [Parameter] public int Version { get; set; }

    private DataTableGroupLevel Level => Table.GroupLevels[Depth];

    // the groups shown stay until the new counts arrive, so a reload does not flash an empty list
    protected override async Task OnParametersSetAsync()
    {
        if (_loaded == (Version, Conditions) || Depth >= Table.GroupLevels.Count)
        {
            return;
        }

        _loaded = (Version, Conditions);
        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        try
        {
            if (await Table.GroupsAsync(Conditions, Level, loading.Token) is { } groups && !loading.IsCancellationRequested)
            {
                _groups = groups;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string Label(object? value) =>
        value is null ? L["(empty)"] : Table.GroupLabel(Level.Path, value) ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    private void OnKey(KeyboardEventArgs args, string conditions)
    {
        if (args.Key is "Enter" or " ")
        {
            Table.ToggleGroup(conditions);
        }
    }

    public void Dispose()
    {
        _loading?.Cancel();
        _loading?.Dispose();
    }
}
