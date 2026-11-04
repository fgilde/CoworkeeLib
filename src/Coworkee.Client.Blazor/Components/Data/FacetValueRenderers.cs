using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The renderers of facet options and table cells by display name; "country" (flag and label) is built in, the last one added for a name wins.</summary>
public sealed class FacetValueRenderers
{
    private readonly Dictionary<string, Func<FacetValueContext, RenderFragment>> _renderers = new(StringComparer.OrdinalIgnoreCase)
    {
        [FacetDisplayAttribute.Country] = new CountryValueRenderer().Render,
    };

    public FacetValueRenderers Add(IFacetValueRenderer renderer) => Add(renderer.Display, renderer.Render);

    public FacetValueRenderers Add(string display, Func<FacetValueContext, RenderFragment> render)
    {
        _renderers[display] = render;
        return this;
    }

    public Func<FacetValueContext, RenderFragment>? Find(string? display) =>
        display is not null && _renderers.TryGetValue(display, out var render) ? render : null;

    /// <summary>The value as its display renders it; <c>null</c> without a renderer for <paramref name="display"/>.</summary>
    public RenderFragment? Render(string? display, FacetValueContext context) => Find(display)?.Invoke(context);
}
