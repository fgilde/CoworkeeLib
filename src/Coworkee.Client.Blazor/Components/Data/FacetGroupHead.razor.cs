using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// The head of a facet group: its title, "any of / all of" where values can hold together (flags, or a group set to all of), the
/// "show as" menu of switchable groups and a clear button.
/// </summary>
public partial class FacetGroupHead
{
    [Parameter] public bool ShowTitle { get; set; } = true;

    [Parameter] public FacetView? View { get; set; }

    [Parameter] public EventCallback<FacetView> ViewChanged { get; set; }

    private FacetView CurrentView => Facets.View(Group, View);

    private IReadOnlyList<FacetView> Views => Group.Switchable ? Facets.Views(Group) : [];

    // "all of" on a scalar ("Status is Open and Shipped") finds nothing, so the switch shows where values can hold together
    private bool CanCombine => Group.Switchable && Combine != FacetCombine.Single && !Facets.IsRange(Group)
        && (Facets.Kind(Group) == FacetValueKind.Flags || Facets.Combine(Group) == FacetCombine.All);

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
