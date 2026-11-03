using System.Reflection;

namespace Coworkee.OData.Transfer.Import;

public sealed record Lookup(string EntitySet, Type EntityType, PropertyInfo Display)
{
    private static readonly string[] DisplayNames = ["Name", "Title", "DisplayName", "Code", "Key"];

    public static PropertyInfo? DisplayOf(Type entity) =>
        DisplayNames.Select(n => entity.GetProperty(n)).FirstOrDefault(p => p?.PropertyType == typeof(string))
        ?? entity.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));
}
