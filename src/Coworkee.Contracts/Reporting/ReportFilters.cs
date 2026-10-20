namespace Coworkee.Contracts.Reporting;

/// <summary>A widget's rows without a browser: its own filter and facets, the report's facets of its set and the date range on its date field (no cross filters).</summary>
public static class ReportFilters
{
    public static string? For(WidgetQuery query, ReportDefinition definition, IReadOnlyList<ReportFieldDto> fields, DateOnly today)
    {
        var date = definition.DateRange is { } range && fields.FirstOrDefault(f => string.Equals(f.Path, query.DateField, StringComparison.OrdinalIgnoreCase)) is { } field
            ? DateRanges.Filter(field.Path, DateRanges.Resolve(range, today), field.DateOnly)
            : null;
        var report = definition.Filters.Where(f => string.Equals(f.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase));
        return ReportViews.And(query.Filter, Facets(query.Facets), Facets(report), date);
    }

    /// <summary>Chosen facets as OData: the values of one group joined by their operator, the groups joined by and.</summary>
    public static string? Facets(IEnumerable<ReportFilterValue> values)
    {
        var groups = values.Where(v => v.OData.Length > 0).GroupBy(v => v.GroupKey, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.First().OData : "(" + string.Join(g.First().Operator == Data.FacetOperator.And ? " and " : " or ", g.Select(v => v.OData)) + ")")
            .ToList();
        return groups.Count == 0 ? null : string.Join(" and ", groups);
    }
}
