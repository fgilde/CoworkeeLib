namespace Coworkee.Contracts.Data;

/// <summary>
/// How the values of a property show in facets and table cells, named by a display the client renders: <see cref="Country"/> is built in
/// (<see cref="CountryCodeAttribute"/>), an app adds its own, e.g. <c>[FacetDisplay("port")]</c> with a renderer for "port" in
/// <c>CoworkeeClientOptions.ValueRenderers</c>. The facet carries it to the client as <see cref="FacetGroupDto.Display"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class FacetDisplayAttribute(string display) : Attribute
{
    /// <summary>A country as ISO 3166 alpha-2 code or name: shown with its flag.</summary>
    public const string Country = "country";

    public string Display { get; } = display;
}
