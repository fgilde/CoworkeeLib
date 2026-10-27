using System.Collections;
using Microsoft.AspNetCore.Http;
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

    public override IODataSerializer GetODataPayloadSerializer(Type type, HttpRequest request) =>
        typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string) ? _sets : base.GetODataPayloadSerializer(type, request);

    public override IODataEdmTypeSerializer GetEdmTypeSerializer(IEdmTypeReference edmType) =>
        edmType.IsEntity() ? _resources : base.GetEdmTypeSerializer(edmType);
}

/// <summary>
/// Leaves out the protected properties the user may not see (<see cref="ODataEntityAccess.DeniedItem"/>) and computed properties the row
/// carries no value of: they have one only when <c>$select</c> names them.
/// </summary>
internal sealed class ComputedResourceSerializer(IODataSerializerProvider provider, ODataComputedProperties computed) : ODataResourceSerializer(provider)
{
    public override ODataProperty CreateStructuralProperty(IEdmStructuralProperty structuralProperty, ResourceContext resourceContext) =>
        (computed.Find(structuralProperty) is null || resourceContext.EdmObject.TryGetPropertyValue(structuralProperty.Name, out _)) && !Denied(structuralProperty, resourceContext)
            ? base.CreateStructuralProperty(structuralProperty, resourceContext)
            : null!;

    // the row's own type and its bases: a property a shared base declares is denied per set
    private static bool Denied(IEdmStructuralProperty property, ResourceContext context)
    {
        if (context.Request?.HttpContext.Items[ODataEntityAccess.DeniedItem] is not Dictionary<string, IReadOnlySet<string>> denied)
        {
            return false;
        }

        for (var type = context.StructuredType; type is not null; type = type.BaseType())
        {
            if (denied.TryGetValue(type.FullTypeName(), out var names) && names.Contains(property.Name))
            {
                return true;
            }
        }

        return false;
    }
}
