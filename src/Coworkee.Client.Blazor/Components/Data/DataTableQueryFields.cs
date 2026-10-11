using System.Text.Json;
using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// The fields a table offers in its query builder: every property column with its title and type (enums with their translated
/// labels), and each reference facet not shown as a column (<c>CustomerId</c>) as a choice of its facet values.
/// </summary>
public static class DataTableQueryFields
{
    public static IReadOnlyList<QueryField> Of<T>(MudDataGrid<T> grid, IReadOnlyList<FacetGroupDto> facets, Localization.CoworkeeLocalizer l)
    {
        var fields = DataGridLayout<T>.Named(grid)
            .Where(DataGridLayout<T>.IsProperty)
            .Select(c => Field(c.PropertyName!, DataGridLayout<T>.Label(c), Property(typeof(T), c.PropertyName!), l))
            .OfType<QueryField>()
            .ToList();
        foreach (var facet in facets.Where(f => f.ValueKind == FacetValueKind.Reference && f.ValuePath is not null))
        {
            var name = facet.ValuePath!.Replace('/', '.');
            if (fields.All(f => !string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                fields.Add(new QueryField(name, l[facet.Label], QueryFieldType.Guid) { Options = [.. facet.Options.Select(o => new QueryOption(Text(o.Value), o.Label))] });
            }
        }

        return fields;
    }

    /// <summary>A field for a property path of <paramref name="type"/>, <c>null</c> for types the query builder cannot filter.</summary>
    public static QueryField? Field(string path, string label, Type? type, Localization.CoworkeeLocalizer l)
    {
        if (type is null)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var nullable = underlying != type || !type.IsValueType;
        var field = FieldType.Identify(underlying);
        if (underlying == typeof(DateTimeOffset) || underlying == typeof(DateTime))
        {
            return new QueryField(path, label, QueryFieldType.DateTime) { Nullable = nullable };
        }

        return field switch
        {
            { IsEnum: true } => new QueryField(path, label, QueryFieldType.Enum)
            {
                Options = [.. Enum.GetValues(underlying).Cast<Enum>().Where(v => !Flagged(underlying) || Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) != 0)
                    .Select(v => new QueryOption(v.ToString(), l[EnumLabels.Of(v)]))],
                Flags = Flagged(underlying),
                Nullable = nullable,
            },
            { IsString: true } => new QueryField(path, label, QueryFieldType.Text),
            { IsNumber: true } => new QueryField(path, label, QueryFieldType.Number) { Nullable = nullable },
            { IsBoolean: true } => new QueryField(path, label, QueryFieldType.Boolean) { Nullable = nullable },
            { IsDateOnly: true } => new QueryField(path, label, QueryFieldType.Date) { Nullable = nullable },
            { IsDateTime: true } => new QueryField(path, label, QueryFieldType.DateTime) { Nullable = nullable },
            { IsGuid: true } => new QueryField(path, label, QueryFieldType.Guid) { Nullable = nullable },
            _ => null,
        };
    }

    private static bool Flagged(Type type) => type.IsDefined(typeof(FlagsAttribute), false);

    // the declared type of a path such as Customer.Name, with Nullable kept (the grid's value type drops it)
    private static Type? Property(Type type, string path)
    {
        Type? current = type;
        foreach (var name in path.Split('.'))
        {
            current = current?.GetProperty(name)?.PropertyType;
        }

        return current;
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}
