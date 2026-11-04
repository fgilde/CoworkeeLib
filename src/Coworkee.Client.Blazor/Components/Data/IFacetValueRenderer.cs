using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Renders the facet options and table cells of one display (the name of a [FacetDisplay]); register it in CoworkeeClientOptions.ValueRenderers.</summary>
public interface IFacetValueRenderer
{
    string Display { get; }

    RenderFragment Render(FacetValueContext context);
}
