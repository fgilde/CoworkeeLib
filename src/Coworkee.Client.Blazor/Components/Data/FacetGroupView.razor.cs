using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// One facet group in the view it is set to (see <see cref="Facets.View"/>), with "any of / all of" and "show as" for switchable groups
/// and a clear button. Used by <see cref="FacetBar"/> and <see cref="FacetPanel"/>; place it alone for a custom layout.
/// </summary>
public partial class FacetGroupView
{
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The view the user picked; <c>null</c> uses the group's.</summary>
    [Parameter] public FacetView? View { get; set; }

    [Parameter] public EventCallback<FacetView> ViewChanged { get; set; }

    private FacetView CurrentView => Facets.View(Group, View);

    private IReadOnlyList<FacetView> Views => Group.Switchable ? Facets.Views(Group) : [];

    private bool CanCombine => Group.Switchable && Combine != FacetCombine.Single && !Facets.IsRange(Group) && Facets.Kind(Group) != FacetValueKind.Boolean;

    private bool HasChoice => Selection.Of(Group.Key).Any();

    private static string ViewName(FacetView view) => view switch
    {
        FacetView.Checkbox => "List",
        FacetView.Chips => "Chips",
        FacetView.Select => "Dropdown",
        FacetView.Buttons => "Buttons",
        FacetView.Swatches => "Swatches",
        FacetView.Tree => "Tree",
        FacetView.Toggle => "Switch",
        FacetView.Range => "Range",
        _ => "Dates",
    };

    private static string ViewIcon(FacetView view) => view switch
    {
        FacetView.Checkbox => Icons.Material.Outlined.Checklist,
        FacetView.Chips => Icons.Material.Outlined.Label,
        FacetView.Select => Icons.Material.Outlined.ArrowDropDownCircle,
        FacetView.Buttons => Icons.Material.Outlined.ViewWeek,
        FacetView.Swatches => Icons.Material.Outlined.Palette,
        FacetView.Tree => Icons.Material.Outlined.AccountTree,
        FacetView.Toggle => Icons.Material.Outlined.ToggleOn,
        FacetView.Range => Icons.Material.Outlined.BarChart,
        _ => Icons.Material.Outlined.DateRange,
    };
}
