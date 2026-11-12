namespace Coworkee.Contracts.Data;

/// <summary>
/// How a facet ([ProvideFacet]) looks and combines its values. Unset values follow the facet: <c>MultiSelect = false</c> means
/// <see cref="FacetCombine.Single"/>, <c>GroupOperator = And</c> means <see cref="FacetCombine.All"/>; the view follows the value type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FacetBehaviorAttribute : Attribute
{
    private FacetCombine? _combine;

    public FacetView View { get; set; }

    /// <summary>How chosen values combine: any of them (or), all of them (and) or only one.</summary>
    public FacetCombine Combine
    {
        get => _combine ?? FacetCombine.Any;
        set => _combine = value;
    }

    /// <summary><see cref="Combine"/> when it was set.</summary>
    public FacetCombine? ConfiguredCombine => _combine;

    /// <summary>Values can be excluded ("not Cancelled") as well as chosen.</summary>
    public bool AllowExclude { get; set; }

    /// <summary>Users may switch between "any of" and "all of" and between the views that fit the values.</summary>
    public bool Switchable { get; set; }

    /// <summary>Splits values into a tree for <see cref="FacetView.Tree"/>, e.g. "Europe / Germany / Berlin".</summary>
    public string? TreeSeparator { get; set; }

    /// <summary>
    /// How the values show in facets: a display name like <see cref="FacetDisplayAttribute.Country"/> or an app's own, as
    /// <see cref="FacetDisplayAttribute"/> on the property sets it. For properties that cannot carry the attribute (a copied model):
    /// <c>services.ConfigureFacet&lt;T&gt;(x =&gt; x.Port, f =&gt; f.Display = "port")</c>. Unset, the property's attribute applies.
    /// </summary>
    public string? Display { get; set; }

    /// <summary>Bars of the histogram of number ranges.</summary>
    public int Buckets { get; set; } = 12;
}
