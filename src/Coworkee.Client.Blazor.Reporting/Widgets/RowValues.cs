using System.Globalization;
using System.Text.Json;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Reads OData rows by path ("Customer/Name") and builds $select and $expand for the paths of a table.</summary>
public static class RowValues
{
    public static object? Get(JsonElement row, string path)
    {
        var current = row;
        foreach (var segment in path.Split('/'))
        {
            if (current.ValueKind != JsonValueKind.Object || !TryGet(current, segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static string Text(object? value, ReportFieldDto? field, Func<string, string> localize, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return value switch
        {
            null => string.Empty,
            bool flag => localize(flag ? "Yes" : "No"),
            double number when field?.Kind == ReportFieldKind.Integer => number.ToString("N0", culture),
            double number => number.ToString("N2", culture),
            string text when field?.Kind == ReportFieldKind.Date && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) =>
                field.DateOnly ? DateOnly.FromDateTime(date.Date).ToString("d", culture) : date.ToLocalTime().ToString("g", culture),
            string text when field?.Kind == ReportFieldKind.Enum => localize(field.MemberLabel(text)),
            _ => value.ToString() ?? string.Empty,
        };
    }

    public static string? Select(IEnumerable<string> paths) =>
        string.Join(',', paths.Where(p => !p.Contains('/')).Append("Id").Distinct());

    public static string? Expand(IEnumerable<string> paths) =>
        string.Join(',', paths.Where(p => p.Contains('/')).GroupBy(p => p[..p.IndexOf('/')])
            .Select(g => $"{g.Key}($select={string.Join(',', g.Select(p => p[(p.IndexOf('/') + 1)..]).Distinct())})")) is { Length: > 0 } expand ? expand : null;

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}
