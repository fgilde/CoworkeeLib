using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// One facet group in the view it is set to (see <see cref="Facets.View"/>) below its <see cref="FacetGroupHead"/>. Used by
/// <see cref="FacetBar"/> and <see cref="FacetPanel"/>; place it alone for a custom layout (render <see cref="FacetStyles"/> once).
/// </summary>
public partial class FacetGroupView
{
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>Off when the owner shows the head itself, e.g. in its own header row.</summary>
    [Parameter] public bool ShowHeader { get; set; } = true;

    /// <summary>The view the user picked; <c>null</c> uses the group's.</summary>
    [Parameter] public FacetView? View { get; set; }

    [Parameter] public EventCallback<FacetView> ViewChanged { get; set; }

    private FacetView CurrentView => Facets.View(Group, View);
}
