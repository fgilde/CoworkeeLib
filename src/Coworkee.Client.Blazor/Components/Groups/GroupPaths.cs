namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>Group codes are paths of segments joined by dots (<c>00001.00002</c>); the ancestors of a group have the leading parts of its code.</summary>
public static class GroupPaths
{
    /// <summary>The codes from the root down to the group itself.</summary>
    public static IEnumerable<string> Prefixes(string code)
    {
        var segments = code.Split('.');
        return Enumerable.Range(1, segments.Length).Select(n => string.Join('.', segments.Take(n)));
    }

    /// <summary>"Customer A / Hamburg" from the groups whose codes lead to <paramref name="code"/>; ancestors missing in <paramref name="groups"/> are left out.</summary>
    public static string Path(string? code, string name, IEnumerable<(string? Code, string Name)> groups)
    {
        if (code is null)
        {
            return name;
        }

        var names = groups.Where(g => g.Code is not null).DistinctBy(g => g.Code).ToDictionary(g => g.Code!, g => g.Name, StringComparer.Ordinal);
        return string.Join(" / ", [.. Prefixes(code).SkipLast(1).Select(names.GetValueOrDefault).OfType<string>(), name]);
    }
}
