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

/// <summary>Leaves out computed properties and the protected properties the user may not see (<see cref="ODataEntityAccess.DeniedItem"/>).</summary>
internal sealed class ComputedResourceSerializer(IODataSerializerProvider provider, ODataComputedProperties computed) : ODataResourceSerializer(provider)
{
    public override ODataProperty CreateStructuralProperty(IEdmStructuralProperty structuralProperty, ResourceContext resourceContext) =>
        computed.Find(structuralProperty) is null && !Denied(structuralProperty, resourceContext) ? base.CreateStructuralProperty(structuralProperty, resourceContext) : null!;

    private static bool Denied(IEdmStructuralProperty property, ResourceContext context) =>
        context.Request?.HttpContext.Items[ODataEntityAccess.DeniedItem] is Dictionary<string, IReadOnlySet<string>> denied
        && denied.TryGetValue(property.DeclaringType.FullTypeName(), out var names)
        && names.Contains(property.Name);
}
