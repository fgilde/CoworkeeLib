using System.Collections;
using System.Globalization;

namespace Coworkee.Client.Blazor.ServiceProxies;

/// <summary>Formats route and query values of generated proxies the way the server binds them.</summary>
public static class ServiceProxyUrl
{
    public static string Segment(object? value) => Uri.EscapeDataString(Format(value));

    public static string Query(params ReadOnlySpan<(string Name, object? Value)> values)
    {
        var parts = new List<string>();
        foreach (var (name, value) in values)
        {
            var items = value is IEnumerable many and not string ? many.Cast<object?>() : [value];
            parts.AddRange(items.OfType<object>().Select(item => $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(Format(item))}"));
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        DateTime or DateTimeOffset or DateOnly or TimeOnly => ((IFormattable)value).ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
