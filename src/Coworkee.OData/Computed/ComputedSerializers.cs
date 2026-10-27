using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Formatter.Serialization;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData.Computed;

/// <summary>Nextended's facet annotation on lists, and entities without their computed properties (they have no value to write).</summary>
internal sealed class ComputedSerializerProvider : ODataSerializerProvider
{
    private readonly FacetResourceSetSerializer _sets;
    private readonly ComputedResourceSerializer _resources;

    public ComputedSerializerProvider(IServiceProvider services, ODataComputedProperties computed)
        : base(services)
    {
        _sets = new FacetResourceSetSerializer(this);
        _resources = new ComputedResourceSerializer(this, computed);
    }

    // /$count answers the number of the (declared) list as raw text
    public override IODataSerializer GetODataPayloadSerializer(Type type, HttpRequest request) =>
        typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string) && request.ODataFeature().Path?.LastSegment is not Microsoft.OData.UriParser.CountSegment
            ? _sets
            : base.GetODataPayloadSerializer(type, request);

    public override IODataEdmTypeSerializer GetEdmTypeSerializer(IEdmTypeReference edmType) =>
        edmType.IsEntity() ? _resources : base.GetEdmTypeSerializer(edmType);
}

/// <summary>
/// Leaves out what the user may not see of a row (<see cref="ODataEntityAccess.DeniedItem"/>: protected, hidden on a shared base, computed without
/// permission), properties and expanded navigations, by the row's own type, and computed properties the row carries no value of: they have one
/// only when <c>$select</c> names them.
/// </summary>
internal sealed class ComputedResourceSerializer(IODataSerializerProvider provider, ODataComputedProperties computed) : ODataResourceSerializer(provider)
{
    public override ODataProperty CreateStructuralProperty(IEdmStructuralProperty structuralProperty, ResourceContext resourceContext) =>
        computed.Find(structuralProperty) is null || resourceContext.EdmObject.TryGetPropertyValue(structuralProperty.Name, out _)
            ? base.CreateStructuralProperty(structuralProperty, resourceContext)
            : null!;

    // the node is kept per type and clause, so each type is cut once; a row through a navigation to a shared base gets the node of its own type
    public override SelectExpandNode CreateSelectExpandNode(ResourceContext resourceContext)
    {
        var node = base.CreateSelectExpandNode(resourceContext);
        if (Denied(resourceContext) is not { Count: > 0 } denied)
        {
            return node;
        }

        foreach (var property in node.SelectedStructuralProperties?.Where(p => denied.Contains(p.Name)).ToList() ?? [])
        {
            node.SelectedStructuralProperties!.Remove(property);
        }

        foreach (var navigation in node.SelectedNavigationProperties?.Where(p => denied.Contains(p.Name)).ToList() ?? [])
        {
            node.SelectedNavigationProperties!.Remove(navigation);
        }

        foreach (var navigation in node.ExpandedProperties?.Keys.Where(p => denied.Contains(p.Name)).ToList() ?? [])
        {
            node.ExpandedProperties!.Remove(navigation);
        }

        foreach (var navigation in node.ReferencedProperties?.Keys.Where(p => denied.Contains(p.Name)).ToList() ?? [])
        {
            node.ReferencedProperties!.Remove(navigation);
        }

        return node;
    }

    // the row's own type and its bases: a member a shared base declares is denied per set
    private static HashSet<string>? Denied(ResourceContext context)
    {
        if (context.Request?.HttpContext.Items[ODataEntityAccess.DeniedItem] is not Dictionary<string, IReadOnlySet<string>> denied)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var type = context.StructuredType; type is not null; type = type.BaseType())
        {
            if (denied.TryGetValue(type.FullTypeName(), out var own))
            {
                names.UnionWith(own);
            }
        }

        return names;
    }
}
