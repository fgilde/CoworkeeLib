using System.Linq.Expressions;

namespace Coworkee.OData;

/// <summary>
/// How one OData route names what it shows: the sets, their rules and their rows are the same on every route, only property names differ.
/// <c>/odata</c> is the default profile (CLR names); <see cref="ODataServiceCollectionExtensions.AddCoworkeeODataRoute"/> adds others.
/// </summary>
public sealed class ODataRouteProfile
{
    private readonly List<(Type Type, string Property, string Name)> _aliases = [];

    internal ODataRouteProfile(string prefix) => Prefix = prefix.Trim('/');

    /// <summary>The route prefix, e.g. <c>odata</c> or <c>rt/odata</c>.</summary>
    public string Prefix { get; }

    /// <summary>Property names start lower case (<c>name</c>, <c>isoAlpha2Code</c>, <c>eta</c>), in output and in the model; input matches any case anyway.</summary>
    public bool LowerCamelCase { get; private set; }

    /// <summary>Facets are built for <c>Prefer: odata.include-annotations="*"</c> too, not only for <c>"cn.facets"</c>; on by default.</summary>
    public bool WildcardFacets { get; private set; } = true;

    /// <summary>Every property name of the route lower camel case, as camelCase JSON clients expect it.</summary>
    public ODataRouteProfile CamelCase()
    {
        LowerCamelCase = true;
        return this;
    }

    /// <summary>
    /// The property under another name on this route, e.g. <c>p.Alias&lt;IAudited&gt;(e =&gt; e.DateCreatedUtc, "creationTime")</c>: <c>$filter</c>, <c>$orderby</c>,
    /// <c>$select</c>, <c>$expand</c>, facets and the output use the alias, and the CLR name is unknown on the route. <typeparamref name="T"/> may be a base
    /// class or an interface: every type of the model assignable to it gets the alias. The rules of the property (hidden, protected) stay.
    /// </summary>
    public ODataRouteProfile Alias<T>(Expression<Func<T, object?>> property, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _aliases.Add((typeof(T), ODataServiceCollectionExtensions.PropertyName(property), name));
        return this;
    }

    /// <summary>Whether <c>Prefer: odata.include-annotations="*"</c> builds facets (<see cref="WildcardFacets"/>).</summary>
    public ODataRouteProfile FacetsOnWildcardPrefer(bool enabled = true)
    {
        WildcardFacets = enabled;
        return this;
    }

    /// <summary>The alias of a property of <paramref name="type"/> on this route, null when it keeps its name; for app JSON that should match the route.</summary>
    public string? AliasOf(Type type, string property) =>
        _aliases.LastOrDefault(a => a.Property == property && a.Type.IsAssignableFrom(type)).Name;

    /// <summary>The aliases of this route as (type, CLR property, alias), in the order they were added.</summary>
    public IReadOnlyList<(Type Type, string Property, string Name)> Aliases => _aliases;

    internal bool RenamesProperties => LowerCamelCase || _aliases.Count > 0;
}
