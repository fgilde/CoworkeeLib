using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Coworkee.Client.Blazor.Data;

public static class TableExport
{
    public static string ToCsv<T>(IEnumerable<T> items)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && IsSimple(p.PropertyType)).ToList();
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(";", properties.Select(p => Escape(p.Name))));
        foreach (var item in items)
        {
            csv.AppendLine(string.Join(";", properties.Select(p => Escape(Format(p.GetValue(item))))));
        }

        return csv.ToString();
    }

    public static string ToJson<T>(IEnumerable<T> items) =>
        JsonSerializer.Serialize(items, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private static bool IsSimple(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(Guid)
            || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly) || underlying == typeof(TimeSpan);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value) => Quote(value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value);

    private static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
