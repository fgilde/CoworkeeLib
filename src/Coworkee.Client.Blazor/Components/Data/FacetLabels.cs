using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>How facet values read: translated for enums, flags, yes/no and presets, as they are for row names.</summary>
public static class FacetLabels
{
    public static string Of(Localization.CoworkeeLocalizer l, FacetGroupDto group, FacetOptionDto option) =>
        group.ValueKind is FacetValueKind.Enum or FacetValueKind.Flags or FacetValueKind.Boolean ? l[option.Label] : option.Label;

    /// <summary>A chosen value as its chip shows it, translated when it is a known text (enum labels, presets).</summary>
    public static string Of(Localization.CoworkeeLocalizer l, SelectedFacet facet) => l.Find(facet.Label) ?? facet.Label;
}
