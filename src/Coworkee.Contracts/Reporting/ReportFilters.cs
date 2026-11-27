namespace Coworkee.Contracts.Reporting;

/// <summary>A widget's rows without a browser: its own filter and facets, the report's facets of its set and the date range on its date field (no cross filters).</summary>
public static class ReportFilters
{
    public static string? For(WidgetQuery query, ReportDefinition definition, IReadOnlyList<ReportFieldDto> fields, DateOnly today) =>
        For(query, definition, fields, today, _ => null);

    /// <summary>With <paramref name="fieldsOf"/> (the fields of an entity set, <c>null</c> when unknown) the report's facets of other sets with the same field apply too.</summary>
    public static string? For(WidgetQuery query, ReportDefinition definition, IReadOnlyList<ReportFieldDto> fields, DateOnly today, Func<string, IReadOnlyList<ReportFieldDto>?> fieldsOf)
    {
        var date = definition.DateRange is { } range && fields.FirstOrDefault(f => string.Equals(f.Path, query.DateField, StringComparison.OrdinalIgnoreCase)) is { } field
            ? DateRanges.Filter(field.Path, DateRanges.Resolve(range, today), field.DateOnly)
            : null;
        var report = Applying(definition.Filters, query.EntitySet, fieldsOf);
        return ReportViews.And(query.Filter, Facets(query.Facets), Facets(report), date);
    }

    /// <summary>
    /// The values that narrow <paramref name="entitySet"/>: its own and those of other sets whose field (<see cref="ReportFilterValue.Path"/>) it has too, with the same kind.
    /// The OData clause is reused as it is, so sets sharing a facet must name the field alike.
    /// </summary>
    public static IEnumerable<ReportFilterValue> Applying(IEnumerable<ReportFilterValue> values, string entitySet, Func<string, IReadOnlyList<ReportFieldDto>?> fieldsOf)
    {
        var here = fieldsOf(entitySet);
        return values.Where(v => string.Equals(v.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase) || Shares(fieldsOf(v.EntitySet), here, v.Path));
    }

    /// <summary>Both sets have the field and it has the same kind in each.</summary>
    public static bool Shares(IReadOnlyList<ReportFieldDto>? from, IReadOnlyList<ReportFieldDto>? to, string? path) =>
        Kind(from, path) is { } kind && Kind(to, path) == kind;

    private static ReportFieldKind? Kind(IReadOnlyList<ReportFieldDto>? fields, string? path) =>
        fields?.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.Kind;

    /// <summary>Chosen facets as OData: the values of one group joined by their operator, the groups joined by and.</summary>
    public static string? Facets(IEnumerable<ReportFilterValue> values)
    {
        var groups = values.Where(v => v.OData.Length > 0).GroupBy(v => v.GroupKey, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.First().OData : "(" + string.Join(g.First().Operator == Data.FacetOperator.And ? " and " : " or ", g.Select(v => v.OData)) + ")")
            .ToList();
        return groups.Count == 0 ? null : string.Join(" and ", groups);
    }
}
