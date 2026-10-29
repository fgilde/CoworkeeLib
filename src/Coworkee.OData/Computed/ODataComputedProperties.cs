using Microsoft.OData.Edm;

namespace Coworkee.OData.Computed;

/// <summary>The computed properties of all sources, added to the EDM model as nullable structural properties.</summary>
public sealed class ODataComputedProperties(ODataEntityRegistry registry, IEnumerable<IODataComputedPropertySource> sources)
{
    private readonly Lazy<IReadOnlyList<ODataComputedProperty>> _all = new(() => [.. sources.SelectMany(s => s.Properties(registry))]);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IEdmProperty, ODataComputedProperty> _edm = new();

    public IReadOnlyList<ODataComputedProperty> All => _all.Value;

    public IEnumerable<ODataComputedProperty> Of(Type entityType) => All.Where(p => p.EntityType == entityType);

    // the properties of every route's model
    internal ODataComputedProperty? Find(IEdmProperty property) => _edm.GetValueOrDefault(property);

    /// <param name="name">The property's name on the route (lower camel case on some).</param>
    internal void AddTo(EdmModel model, Func<string, string> name)
    {
        foreach (var property in All)
        {
            if (model.FindDeclaredType(property.EntityType.FullName) is EdmEntityType type)
            {
                var kind = EdmCoreModel.Instance.GetPrimitiveType(Kind(property.Type));
                _edm[type.AddStructuralProperty(name(property.Name), new EdmPrimitiveTypeReference(kind, true))] = property;
            }
        }
    }

    private static EdmPrimitiveTypeKind Kind(Type type) => (Nullable.GetUnderlyingType(type) ?? type) switch
    {
        var t when t == typeof(bool) => EdmPrimitiveTypeKind.Boolean,
        var t when t == typeof(int) => EdmPrimitiveTypeKind.Int32,
        var t when t == typeof(long) => EdmPrimitiveTypeKind.Int64,
        var t when t == typeof(decimal) => EdmPrimitiveTypeKind.Decimal,
        var t when t == typeof(double) => EdmPrimitiveTypeKind.Double,
        var t when t == typeof(Guid) => EdmPrimitiveTypeKind.Guid,
        var t when t == typeof(DateTimeOffset) => EdmPrimitiveTypeKind.DateTimeOffset,
        _ => EdmPrimitiveTypeKind.String,
    };
}
