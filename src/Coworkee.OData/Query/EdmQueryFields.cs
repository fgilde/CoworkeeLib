using System.Globalization;
using Coworkee.Contracts.Query;
using Microsoft.OData.Edm;

namespace Coworkee.OData.Query;

/// <summary>The query fields of an entity set, read from the EDM model: what <see cref="ODataQueryTranslator"/> needs to turn a saved query into a $filter on the server.</summary>
public static class EdmQueryFields
{
    /// <summary>
    /// The properties of the set and, one level deep, those of its single-valued navigations (<c>Customer.Name</c>). Properties only some users see
    /// are part of the model and so listed; hidden ones are not in the model.
    /// </summary>
    public static IReadOnlyList<QueryField> For(IEdmModel model, string entitySet)
    {
        var set = model.EntityContainer.FindEntitySet(entitySet) ?? throw new ArgumentException($"The model has no entity set '{entitySet}'.", nameof(entitySet));
        var type = set.EntityType;
        var fields = Properties(type, prefix: null).ToList();
        foreach (var navigation in type.NavigationProperties().Where(n => !n.Type.IsCollection()))
        {
            fields.AddRange(Properties(navigation.ToEntityType(), navigation.Name + "."));
        }

        return fields;
    }

    private static IEnumerable<QueryField> Properties(IEdmStructuredType type, string? prefix) =>
        type.Properties().OfType<IEdmStructuralProperty>().Select(p => Field(prefix + p.Name, p)).OfType<QueryField>();

    private static QueryField? Field(string name, IEdmStructuralProperty property)
    {
        var reference = property.Type;
        var nullable = reference.IsNullable;
        var definition = reference.Definition;
        if (definition is IEdmEnumType enumType)
        {
            return new QueryField(name, name, QueryFieldType.Enum)
            {
                Flags = enumType.IsFlags,
                Nullable = nullable,
                Options = [.. enumType.Members.Where(m => !enumType.IsFlags || m.Value.Value != 0).Select(m => new QueryOption(m.Name, m.Name))],
            };
        }

        var kind = reference.PrimitiveKind() switch
        {
            EdmPrimitiveTypeKind.String => QueryFieldType.Text,
            EdmPrimitiveTypeKind.Boolean => QueryFieldType.Boolean,
            EdmPrimitiveTypeKind.Guid => QueryFieldType.Guid,
            EdmPrimitiveTypeKind.Date => QueryFieldType.Date,
            EdmPrimitiveTypeKind.DateTimeOffset => QueryFieldType.DateTime,
            EdmPrimitiveTypeKind.Byte or EdmPrimitiveTypeKind.SByte or EdmPrimitiveTypeKind.Int16 or EdmPrimitiveTypeKind.Int32 or EdmPrimitiveTypeKind.Int64
                or EdmPrimitiveTypeKind.Single or EdmPrimitiveTypeKind.Double or EdmPrimitiveTypeKind.Decimal => QueryFieldType.Number,
            _ => (QueryFieldType?)null,
        };
        return kind is { } type ? new QueryField(name, name, type) { Nullable = nullable } : null;
    }
}
