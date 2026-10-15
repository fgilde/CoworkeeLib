using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Coworkee.Contracts.Data;

namespace Coworkee.OData.Transfer.Import;

/// <summary>
/// A writable property of the import's row type. A Guid property "BrandId" next to a navigation "Brand" of a registered set
/// is a lookup: the file may name the brand by its display text instead of its id.
/// </summary>
public sealed record ImportField(string Name, string Label, Type Type, bool Required, PropertyInfo? Property, Lookup? Lookup = null)
{
    public const string Id = "Id";

    public bool KeyOnly => Property is null;

    public ImportFieldDto ToDto() => new(
        Name,
        Label,
        Underlying.IsEnum ? "Enum" : Underlying.Name,
        Required,
        Underlying.IsEnum ? Enum.GetNames(Underlying) : null,
        Lookup?.EntitySet,
        KeyOnly);

    public Type Underlying => Nullable.GetUnderlyingType(Type) ?? Type;

    public static IReadOnlyList<ImportField> Of(ODataImportRegistration import, ODataEntityRegistry registry)
    {
        var entity = registry.Find(import.EntitySet)?.EntityType;
        var fields = import.RowType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsSimple(p.PropertyType) && (p.CanWrite || HasConstructorParameter(import.RowType, p.Name)))
            .Select(p => new ImportField(p.Name, LabelOf(p, entity), p.PropertyType, IsRequired(p), p, LookupOf(p, entity, registry)))
            .ToList();
        if (import.Update is not null && fields.All(f => f.Name != Id))
        {
            fields.Insert(0, new ImportField(Id, Id, typeof(Guid), false, null));
        }

        return fields;
    }

    public static bool IsSimple(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(Guid)
               || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly) || underlying == typeof(TimeOnly)
               || underlying == typeof(TimeSpan);
    }

    private static bool HasConstructorParameter(Type type, string name) =>
        type.GetConstructors().Any(c => c.GetParameters().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    private static bool IsRequired(PropertyInfo property) =>
        property.GetCustomAttribute<RequiredAttribute>() is not null
        || property.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() is not null;

    private static string LabelOf(PropertyInfo property, Type? entity) =>
        property.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } display ? display
        : property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName is { Length: > 0 } displayName ? displayName
        : property.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } description ? description
        : Navigation(property, entity) is { } navigation ? EnumLabels.Humanize(navigation.Name)
        : EnumLabels.Humanize(property.Name);

    private static PropertyInfo? Navigation(PropertyInfo property, Type? entity) =>
        entity is not null && property.Name.Length > 2 && property.Name.EndsWith(Id, StringComparison.Ordinal)
        && (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) == typeof(Guid)
            ? entity.GetProperty(property.Name[..^2])
            : null;

    private static Lookup? LookupOf(PropertyInfo property, Type? entity, ODataEntityRegistry registry) =>
        Navigation(property, entity) is { } navigation && registry.Find(navigation.PropertyType) is { } target && Lookup.DisplayOf(target.EntityType) is { } display
            ? new Lookup(target.EntitySet, target.EntityType, display)
            : null;
}

public sealed record Lookup(string EntitySet, Type EntityType, PropertyInfo Display)
{
    private static readonly string[] DisplayNames = ["Name", "Title", "DisplayName", "Code", "Key"];

    public static PropertyInfo? DisplayOf(Type entity) =>
        DisplayNames.Select(n => entity.GetProperty(n)).FirstOrDefault(p => p?.PropertyType == typeof(string))
        ?? entity.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));
}
