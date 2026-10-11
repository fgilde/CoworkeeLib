using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Facets as a row of pills, each opening its group in a popover; a pill shows the choice at a glance. The layout of tables;
/// <see cref="FacetPanel"/> lists the groups open in a side panel instead.
/// </summary>
public partial class FacetBar
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<FacetGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    /// <summary>The view the user picked per group key; the bar writes picks into it and raises <see cref="ViewsChanged"/>.</summary>
    [Parameter] public IDictionary<string, FacetView>? Views { get; set; }

    [Parameter] public EventCallback ViewsChanged { get; set; }

    private FacetView? ViewOf(FacetGroupDto group) => Views?.TryGetValue(group.Key, out var view) == true ? view : null;

    private Task SetViewAsync(FacetGroupDto group, FacetView view)
    {
        if (Views is not null)
        {
            Views[group.Key] = view;
        }

        return ViewsChanged.InvokeAsync();
    }

    private string Summary(IReadOnlyList<SelectedFacet> chosen)
    {
        var first = chosen[0];
        var text = (first.Exclude ? L["not"] + " " : string.Empty) + FacetLabels.Of(L, first);
        return chosen.Count == 1 ? text : $"{text}, …";
    }

    internal static string Icon(FacetGroupDto group) => Facets.Kind(group) switch
    {
        FacetValueKind.Date => Icons.Material.Outlined.Event,
        FacetValueKind.Number => Icons.Material.Outlined.Straighten,
        FacetValueKind.Boolean => Icons.Material.Outlined.ToggleOn,
        FacetValueKind.Enum or FacetValueKind.Flags => Icons.Material.Outlined.Label,
        FacetValueKind.Reference => Icons.Material.Outlined.Link,
        _ => Icons.Material.Outlined.FilterList,
    };
}
