using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Counts the server's facet options on local rows. Options on one property (values, ranges, flags) share one pass over the column's ranks:
/// the rows are counted per distinct value once, each option adds up the values it accepts.
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
        var counted = new Dictionary<Column, int[]>();
        var options = new List<FacetOptionDto>();
        foreach (var option in group.Options)
        {
            var count = LocalFilter.OnColumn(table, ODataLinq.Parse(option.OData)) is { } single
                ? single.Ranks.Sum(counted.TryGetValue(single.Column, out var known) ? known : counted[single.Column] = single.Column.Prefix(rows))
                : Matching(table.Mask(option.OData), rows);

            // the server lists the values found in the rows, but keeps every range preset
            if (count > 0 || group.Type is FacetKind.Range or FacetKind.DateRange)
            {
                options.Add(option with { Count = count });
            }
        }

        return options;
    }

    private static int Matching(bool[] mask, int[] rows)
    {
        var count = 0;
        foreach (var row in rows)
        {
            if (mask[row])
            {
                count++;
            }
        }

        return count;
    }
}
