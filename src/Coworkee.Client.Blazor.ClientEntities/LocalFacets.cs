using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Counts the server's facet options on local rows: "Path eq value" options of one path share a single pass over the column's ranks.
/// With a facet context an "any of" group with a choice counts on the rows of every other choice but its own, as the server does.
/// </summary>
internal static class LocalFacets
{
    public static IReadOnlyList<FacetGroupDto> Count<T>(IReadOnlyList<FacetGroupDto> groups, LocalTable<T> table, int[] rows, FacetContextDto? context = null) =>
        [.. groups.Select(group => group with { Options = Options(group, table, Own(context, group.Key) ? table.Find(context!.Without(group.Key), null) : rows) })];

    private static bool Own(FacetContextDto? context, string group) =>
        context is not null && context.Groups.ContainsKey(group) && context.Combines?.GetValueOrDefault(group) != FacetCombine.All;

    private static List<FacetOptionDto> Options<T>(FacetGroupDto group, LocalTable<T> table, int[] rows)
    {
        var byPath = new Dictionary<string, (Column Column, int[] Counts)>(StringComparer.OrdinalIgnoreCase);
        var options = new List<FacetOptionDto>();
        foreach (var option in group.Options)
        {
            int count;
            if (ODataLinq.TryEquality<T>(option.OData, out var path, out var value) && value is not null)
            {
                if (!byPath.TryGetValue(path, out var counted))
                {
                    var column = table.Column(path);
                    byPath[path] = counted = (column, column.Count(rows));
                }

                count = counted.Column.RankOf(value) is var rank and >= 0 ? counted.Counts[rank] : 0;
            }
            else
            {
                var mask = table.Mask(option.OData);
                count = rows.Count(row => mask[row]);
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
