namespace Coworkee.Contracts.Data;

/// <summary>The property holds a country (ISO 3166 alpha-2 code like "DE", or a name): facets and table cells show its flag.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CountryCodeAttribute() : FacetDisplayAttribute(Country);
