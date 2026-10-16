using System.Collections.Concurrent;
using System.Reflection;

namespace Coworkee.Client.Blazor.Search;

/// <summary>Icons of search hits: a Material icon name (<c>Person</c>) becomes the outlined MudBlazor icon, SVG markup stays as it is.</summary>
public static class SearchIcons
{
    private static readonly ConcurrentDictionary<string, string?> Named = new(StringComparer.OrdinalIgnoreCase);

    public static string Resolve(string? icon) => icon switch
    {
        null or "" => MudBlazor.Icons.Material.Outlined.Search,
        _ when icon.TrimStart().StartsWith('<') => icon,
        _ => Named.GetOrAdd(icon, name => typeof(MudBlazor.Icons.Material.Outlined)
            .GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)?.GetValue(null) as string)
            ?? MudBlazor.Icons.Material.Outlined.Search,
    };
}
