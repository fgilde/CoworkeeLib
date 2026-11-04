using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The built-in "country" display: the flag of the value (a code or a name, else the label) before the label.</summary>
public sealed class CountryValueRenderer : IFacetValueRenderer
{
    public string Display => FacetDisplayAttribute.Country;

    public RenderFragment Render(FacetValueContext context) => builder =>
    {
        if ((CountryCodes.Resolve(context.Value) ?? CountryCodes.Resolve(context.Label)) is { } code)
        {
            builder.AddContent(0, FlagImages.Image(code, 14, string.Empty, "margin-inline-end:6px"));
        }

        builder.AddContent(1, context.Label);
    };
}
