using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as color swatches: the option's color ([FacetColor] on enum members, a "color" meta value or a CSS color as value), else one of a palette.</summary>
public partial class FacetSwatchView
{
    private static readonly string[] Palette = ["#1565c0", "#00838f", "#2e7d32", "#ef6c00", "#6a1b9a", "#c62828", "#00695c", "#4527a0", "#f9a825", "#5d4037"];

    private string Color(FacetOptionDto option)
    {
        if (option.Meta?.TryGetValue("color", out var color) == true)
        {
            return color;
        }

        var value = option.Value.ValueKind == System.Text.Json.JsonValueKind.String ? option.Value.GetString() : null;
        if (value is { Length: > 0 } && (value[0] == '#' || value.All(char.IsLetter)) && value.Length <= 20)
        {
            return value;
        }

        return Palette[(int)((uint)StringComparer.Ordinal.GetHashCode(option.OData) % Palette.Length)];
    }
}
