using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Placed after the columns of a grid: Blazor sets its parameters right after theirs and before the grid renders its rows,
/// so it gives the property columns through navigations their null safe getters again (<see cref="CoworkeeDataTable{T}.FitNullSafe"/>).
/// </summary>
public sealed class DataTableNullSafeColumns<T> : ComponentBase
{
    [CascadingParameter] public MudDataGrid<T>? Grid { get; set; }

    /// <summary>Any object: a parameter of a reference type makes Blazor set the parameters each time the grid renders its columns.</summary>
    [Parameter] public object? Table { get; set; }

    protected override void OnParametersSet()
    {
        foreach (var column in Grid?.RenderedColumns ?? [])
        {
            CoworkeeDataTable<T>.FitNullSafe(column);
        }
    }

    protected override bool ShouldRender() => false;
}
