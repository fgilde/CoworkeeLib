using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Coworkee.OData;

/// <summary>Builds the EDM model of one route from the registry; names follow the route's profile, everything else is the same on every route.</summary>
internal static class ODataModel
{
    private static readonly HashSet<Type> Primitives =
    [
        typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal),
        typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(byte[]),
    ];

    /// <param name="findShared">The default route only: its build finds the hidden properties every route denies (<see cref="ODataEntityRegistration.SharedHidden"/>).</param>
    /// <param name="entityFramework">The EF entity type of a CLR type (<see cref="CoworkeeODataOptions.ModelFromEntityFramework"/>): keys and properties come from it.</param>
    public static IEdmModel Build(
        ODataRouteProfile profile, ODataEntityRegistry registry, Computed.ODataComputedProperties computed, Func<Type, IEntityType?>? entityFramework, bool findShared, ILogger? logger)
    {
        var builder = new ODataConventionModelBuilder();
        foreach (var entity in registry.Entities)
        {
            FacetResponse.RefuseCollectionPaths(entity.EntityType);
            var type = builder.AddEntityType(entity.EntityType);
            type.HasKey(entity.EntityType.GetProperty(entity.Key) ?? throw new InvalidOperationException($"{entity.EntityType.Name} needs a {entity.Key} property as the key of its OData set."));
            foreach (var hidden in entity.HiddenProperties)
            {
                type.RemoveProperty(entity.EntityType.GetProperty(hidden)!);
            }

            builder.AddEntitySet(entity.EntitySet, type);
        }

        if (entityFramework is not null)
        {
            KeysFromEntityFramework(builder, registry, entityFramework);
        }

        builder.OnModelCreating += b =>
        {
            if (entityFramework is not null)
            {
                PropertiesFromEntityFramework(b, registry, entityFramework, findShared ? logger : null);
            }

            if (profile.LowerCamelCase)
            {
                new LowerCamelCaser().ApplyLowerCamelCase(b);
            }

            foreach (var property in b.StructuralTypes.SelectMany(t => t.Properties.Select(p => (Type: t, Property: p))).Where(p => p.Property.PropertyInfo is not null))
            {
                if (profile.AliasOf(property.Type.ClrType, property.Property.PropertyInfo.Name) is { } alias)
                {
                    property.Property.Name = alias;
                }
            }
        };

        var model = (EdmModel)builder.GetEdmModel();
        if (findShared)
        {
            FindSharedHidden(registry, model, logger);
        }

        computed.AddTo(model, profile.LowerCamelCase ? new LowerCamelCaser().ToLowerCamelCase : name => name);
        if (profile.RenamesProperties)
        {
            RefuseDuplicates(profile, model);
        }

        return model;
    }

    // names match regardless of case in URIs: two members of one name would leave it to the parser which one a query reads
    private static void RefuseDuplicates(ODataRouteProfile profile, IEdmModel model)
    {
        foreach (var type in model.SchemaElements.OfType<IEdmStructuredType>())
        {
            if (type.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            {
                throw new InvalidOperationException($"The OData route {profile.Prefix} names two members of {type.FullTypeName()} {twice.Key}; choose another alias.");
            }
        }
    }

    // hidden properties a base type declares that other sets share stay in the model; they are denied on the set that hides them
    private static void FindSharedHidden(ODataEntityRegistry registry, EdmModel model, ILogger? logger)
    {
        foreach (var entity in registry.Entities)
        {
            var type = (IEdmStructuredType)model.FindDeclaredType(entity.EntityType.FullName);
            foreach (var hidden in entity.HiddenProperties.Where(h => type.FindProperty(h) is not null))
            {
                entity.SharedHidden.Add(hidden);
                logger?.LogWarning(
                    "{Set}: {Property} is declared on {Base}, a type the OData model shares with other sets, so Hide cannot remove it; it stays in the model and is refused on {Set} for everyone.",
                    entity.EntitySet, hidden, type.FindProperty(hidden).DeclaringType.FullTypeName(), entity.EntitySet);
            }
        }
    }

    /// <summary>
    /// The entity types the sets reach through EF navigations, keyed by their EF primary key: the convention would take <c>{Type}Id</c> next to an
    /// inherited <c>Id</c> as a second key candidate, find none and turn the type into a complex type. Registered sets keep their own key.
    /// </summary>
    private static void KeysFromEntityFramework(ODataConventionModelBuilder builder, ODataEntityRegistry registry, Func<Type, IEntityType?> entityFramework)
    {
        var seen = new HashSet<IEntityType>();
        var queue = new Queue<IEntityType>(registry.Entities.Select(e => entityFramework(e.EntityType)).OfType<IEntityType>());
        while (queue.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            var hidden = registry.Find(type.ClrType)?.HiddenProperties ?? [];
            foreach (var navigation in type.GetNavigations().Cast<INavigationBase>().Concat(type.GetSkipNavigations())
                .Where(n => n.PropertyInfo is not null && !hidden.Contains(n.Name)))
            {
                foreach (var target in navigation.TargetEntityType.GetDerivedTypesInclusive().Where(t => !t.IsOwned()))
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var type in seen.Where(t => t.BaseType is null && !t.HasSharedClrType && registry.Find(t.ClrType) is null))
        {
            if (type.FindPrimaryKey() is { } key && key.Properties.All(p => p.PropertyInfo is not null))
            {
                var configuration = builder.AddEntityType(type.ClrType);
                foreach (var property in key.Properties)
                {
                    configuration.HasKey(Declared(property.PropertyInfo!));
                }
            }
        }
    }

    /// <summary>
    /// Properties EF maps that the convention leaves out (no public setter: read only or computed in C#), on the types of registered sets only: their
    /// registration can hide them. Navigation targets without a set keep the convention's members. Each one added is logged.
    /// </summary>
    private static void PropertiesFromEntityFramework(ODataConventionModelBuilder builder, ODataEntityRegistry registry, Func<Type, IEntityType?> entityFramework, ILogger? logger)
    {
        foreach (var configuration in builder.StructuralTypes.Where(t => registry.Find(t.ClrType) is not null).ToList())
        {
            if (entityFramework(configuration.ClrType) is not { } type)
            {
                continue;
            }

            foreach (var property in type.GetProperties().Where(p => p.PropertyInfo is not null && !p.IsShadowProperty() && !p.IsIndexerProperty()))
            {
                var info = Declared(property.PropertyInfo!);
                var clr = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
                if ((!clr.IsEnum && !Primitives.Contains(clr)) || Known(builder, configuration, info.Name)
                    || registry.Entities.Any(e => (e.EntityType.IsAssignableTo(configuration.ClrType) || configuration.ClrType.IsAssignableTo(e.EntityType)) && e.HiddenProperties.Contains(info.Name)))
                {
                    continue;
                }

                // a base type the model maps on its own would carry it to types without a set
                if (builder.StructuralTypes.Any(t => t.ClrType == info.DeclaringType && t != configuration))
                {
                    continue;
                }

                if (clr.IsEnum)
                {
                    configuration.AddEnumProperty(info);
                }
                else
                {
                    configuration.AddProperty(info);
                }

                logger?.LogWarning("OData model from EF: {Type}.{Property} has no public setter and is in the model now; hide it if users must not see it.", configuration.ClrType.Name, info.Name);
            }
        }
    }

    // the type, its mapped bases and derived types have the property, or the convention ignores it ([NotMapped], [IgnoreDataMember]); hidden ones are checked by the caller
    private static bool Known(ODataConventionModelBuilder builder, StructuralTypeConfiguration configuration, string name) =>
        builder.StructuralTypes
            .Where(t => t.ClrType.IsAssignableFrom(configuration.ClrType) || configuration.ClrType.IsAssignableFrom(t.ClrType))
            .Any(t => t.Properties.Any(p => p.PropertyInfo?.Name == name) || t.IgnoredProperties.Any(p => p.Name == name));

    private static PropertyInfo Declared(PropertyInfo property) =>
        property.DeclaringType!.GetProperty(property.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) ?? property;
}
