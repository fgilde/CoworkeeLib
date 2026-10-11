using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Facets as a side panel of collapsible sections, each group in its view. Groups with a choice and the first <see cref="OpenFirst"/>
/// start open. Pass the groups and a <see cref="FacetSelection"/>; the selection's <c>ToFilter()</c> is the OData filter.
/// </summary>
public partial class FacetPanel
{
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<FacetGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    [Parameter] public IDictionary<string, FacetView>? Views { get; set; }

    [Parameter] public EventCallback ViewsChanged { get; set; }

    [Parameter] public bool ShowHeader { get; set; } = true;

    [Parameter] public int OpenFirst { get; set; } = 4;

    private bool IsOpen(FacetGroupDto group) => _open.TryGetValue(group.Key, out var open)
        ? open
        : Selection.Of(group.Key).Any() || Groups.OrderBy(g => g.Order).Take(OpenFirst).Contains(group);

    private void Toggle(FacetGroupDto group) => _open[group.Key] = !IsOpen(group);

    private FacetView? ViewOf(FacetGroupDto group) => Views?.TryGetValue(group.Key, out var view) == true ? view : null;

    private Task SetViewAsync(FacetGroupDto group, FacetView view)
    {
        if (Views is not null)
        {
            Views[group.Key] = view;
        }

        return ViewsChanged.InvokeAsync();
    }
}
