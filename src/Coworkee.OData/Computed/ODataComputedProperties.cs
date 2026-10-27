using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;

namespace Coworkee.OData.Computed;

/// <summary>The computed properties of all sources, added to the EDM model as nullable structural properties.</summary>
public sealed class ODataComputedProperties(ODataEntityRegistry registry, IEnumerable<IODataComputedPropertySource> sources)
{
    private readonly Lazy<IReadOnlyList<ODataComputedProperty>> _all = new(() => [.. sources.SelectMany(s => s.Properties(registry))]);
    private readonly Dictionary<IEdmProperty, ODataComputedProperty> _edm = [];

    public IReadOnlyList<ODataComputedProperty> All => _all.Value;

    public IEnumerable<ODataComputedProperty> Of(Type entityType) => All.Where(p => p.EntityType == entityType);

    internal ODataComputedProperty? Find(IEdmProperty property) => _edm.GetValueOrDefault(property);

    internal void AddTo(EdmModel model)
    {
        foreach (var property in All)
        {
            if (model.FindDeclaredType(property.EntityType.FullName) is EdmEntityType type)
            {
                var kind = EdmCoreModel.Instance.GetPrimitiveType(Kind(property.Type));
                _edm[type.AddStructuralProperty(property.Name, new EdmPrimitiveTypeReference(kind, true))] = property;
            }
        }
    }

    /// <summary>
    /// Whether one of the texts (a $filter, an $orderby) names a computed or protected property of the set the user lacks the permission of,
    /// or a protected property of another set through a navigation path (Manager/Salary).
    /// </summary>
    // ponytail: a word match, so a text literal with the exact name also counts; parse the clauses if that ever bites
    internal async Task<bool> DeniedAsync(IServiceProvider services, Type entityType, CancellationToken cancellationToken, params string?[] texts)
    {
        foreach (var protectedSet in registry.Entities.Where(e => e.HasDenials))
        {
            var prefix = protectedSet.EntityType == entityType ? @"\b" : "/";
            foreach (var name in await ODataEntityAccess.DeniedPropertiesAsync(services, protectedSet.EntityType, cancellationToken))
            {
                if (texts.Any(t => t is not null && Regex.IsMatch(t, $@"{prefix}{name}\b", RegexOptions.IgnoreCase)))
                {
                    return true;
                }
            }
        }

        foreach (var property in Of(entityType).Where(p => p.Permission is not null))
        {
            if (texts.Any(t => t is not null && Regex.IsMatch(t, $@"\b{property.Name}\b", RegexOptions.IgnoreCase))
                && !await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(property.Permission!, cancellationToken))
            {
                return true;
            }
        }

        return false;
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
