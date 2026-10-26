using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;

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

    /// <summary>The paths of these groups, from two queries: the groups, then their ancestors by code.</summary>
    public static async Task<Dictionary<Guid, string>> OfAsync(IODataClient odata, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var groups = (await QueryAsync(odata, $"Id in ({string.Join(",", ids)})", cancellationToken)).Where(g => ids.Contains(g.Id)).ToList();
        var prefixes = groups.Where(g => g.Code is not null).SelectMany(g => Prefixes(g.Code!).SkipLast(1)).Distinct().ToList();
        var ancestors = prefixes.Count == 0 ? [] : await QueryAsync(odata, $"Code in ({string.Join(",", prefixes.Select(p => $"'{p}'"))})", cancellationToken);
        var named = ancestors.Select(a => (a.Code, a.Name)).ToList();
        return groups.ToDictionary(g => g.Id, g => Path(g.Code, g.Name, named));
    }

    private static async Task<IReadOnlyList<GroupRow>> QueryAsync(IODataClient odata, string filter, CancellationToken cancellationToken) =>
        (await odata.QueryAsync<GroupRow>("Groups", new ODataQuery { Filter = filter, Select = "Id,Name,Code", Count = false }, cancellationToken)).Items;
}
