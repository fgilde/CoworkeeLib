using System.Globalization;

namespace Coworkee.Client.Blazor.Data;

public sealed record ODataQuery
{
    public string? Filter { get; init; }

    public string? Search { get; init; }

    public string? OrderBy { get; init; }

    public string? Expand { get; init; }

    public int? Top { get; init; }

    public int? Skip { get; init; }

    public bool Count { get; init; } = true;

    public bool Facets { get; init; }

    public string ToQueryString()
    {
        var parts = new List<string>();
        Add(parts, "$filter", Filter);
        Add(parts, "$search", Search);
        Add(parts, "$orderby", OrderBy);
        Add(parts, "$expand", Expand);
        Add(parts, "$top", Top?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "$skip", Skip is > 0 ? Skip.Value.ToString(CultureInfo.InvariantCulture) : null);
        if (Count)
        {
            parts.Add("$count=true");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}
