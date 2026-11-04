using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>How facet values read: translated for enums, flags, yes/no and presets, as they are for row names.</summary>
public static class FacetLabels
{
    public static string Of(Localization.CoworkeeLocalizer l, FacetGroupDto group, FacetOptionDto option) =>
        group.ValueKind is FacetValueKind.Enum or FacetValueKind.Flags or FacetValueKind.Boolean ? l[option.Label] : option.Label;

    /// <summary>A chosen value as its chip shows it, translated when it is a known text (enum labels, presets).</summary>
    public static string Of(Localization.CoworkeeLocalizer l, SelectedFacet facet) => l.Find(facet.Label) ?? facet.Label;

    /// <summary>An option as the renderer of the group's display shows it, else its label as text.</summary>
    public static RenderFragment Render(FacetValueRenderers renderers, FacetGroupDto group, FacetOptionDto option, string label) =>
        renderers.Render(group.Display, new FacetValueContext(Text(option.Value), label) { Meta = option.Meta }) ?? (b => b.AddContent(0, label));

    /// <summary>A chosen value as the renderer of its display shows it, else its label as text.</summary>
    public static RenderFragment Render(FacetValueRenderers renderers, SelectedFacet facet, string label) =>
        renderers.Render(facet.Display, new FacetValueContext(null, label)) ?? (b => b.AddContent(0, label));

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };
}
