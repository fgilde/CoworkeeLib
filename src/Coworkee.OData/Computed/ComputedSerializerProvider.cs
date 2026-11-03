using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Formatter.Serialization;
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
