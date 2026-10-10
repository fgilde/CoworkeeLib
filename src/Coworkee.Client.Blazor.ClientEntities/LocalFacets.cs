using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Counts the server's facet options on local rows; "Path eq value" options of one path share a single pass.</summary>
internal static class LocalFacets
{
    public static IReadOnlyList<FacetGroupDto> Count<T>(IReadOnlyList<FacetGroupDto> groups, IReadOnlyList<T> rows, Func<string, Func<T, bool>> compile) =>
        [.. groups.Select(group => group with { Options = Options(group, rows, compile) })];

    private static List<FacetOptionDto> Options<T>(FacetGroupDto group, IReadOnlyList<T> rows, Func<string, Func<T, bool>> compile)
    {
        var byPath = new Dictionary<string, Dictionary<object, int>>(StringComparer.OrdinalIgnoreCase);
        var options = new List<FacetOptionDto>();
        foreach (var option in group.Options)
        {
            int count;
            if (ODataLinq.TryEquality<T>(option.OData, out var path, out var value) && value is not null)
            {
                if (!byPath.TryGetValue(path, out var counts))
                {
                    var getter = ODataLinq.Path<T>(path);
                    counts = rows.Select(getter).OfType<object>().GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                    byPath[path] = counts;
                }

                count = counts.GetValueOrDefault(value);
            }
            else
            {
                count = rows.Count(compile(option.OData));
            }

            // the server lists the values found in the rows, but keeps every range preset
            if (count > 0 || group.Type is FacetKind.Range or FacetKind.DateRange)
            {
                options.Add(option with { Count = count });
            }
        }

        return options;
    }
}
