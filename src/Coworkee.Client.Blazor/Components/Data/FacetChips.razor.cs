using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The chosen facets and column filters as removable chips, with a button that clears them all.</summary>
public partial class FacetChips
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    /// <summary>Column filters, one chip per column.</summary>
    [Parameter] public IReadOnlyList<FilterChip> Filters { get; set; } = [];

    [Parameter] public EventCallback<FilterChip> OnRemove { get; set; }

    /// <summary>Replaces clearing the facets, for owners that clear more than them.</summary>
    [Parameter] public EventCallback OnClear { get; set; }

    private Task ClearAsync()
    {
        if (OnClear.HasDelegate)
        {
            return OnClear.InvokeAsync();
        }

        Selection.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>A column filter as a chip: the column's key and title and the filter as text.</summary>
public sealed record FilterChip(string Key, string Label, string Text);
