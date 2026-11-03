namespace Coworkee.Contracts.Reporting;

/// <summary>Aggregates an entity set on the server: rows filtered by OData, grouped by up to two fields and reduced to values.</summary>
public sealed record ReportQueryRequest(
    string EntitySet,
    string? Filter = null,
    ReportGroupBy? GroupBy = null,
    ReportGroupBy? SplitBy = null,
    IReadOnlyList<ReportAggregate>? Aggregates = null,
    ReportSortBy SortBy = ReportSortBy.Key,
    bool Descending = false,
    int? Top = null)
{
    public static ReportQueryRequest From(WidgetQuery query, string? filter) =>
        new(query.EntitySet, filter, query.GroupBy, query.SplitBy, query.Aggregates, query.SortBy, query.Descending, query.Top);
}
