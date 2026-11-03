namespace Coworkee.Contracts.Data;

/// <summary>The color of an enum member in facet swatches, e.g. <c>[FacetColor("#2e7d32")]</c> or a CSS color name.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class FacetColorAttribute(string color) : Attribute
{
    public string Color { get; } = color;
}
