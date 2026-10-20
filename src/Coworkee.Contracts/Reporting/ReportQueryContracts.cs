using System.Text.Json.Serialization;

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

/// <summary>One value per aggregate of the request, in its order.</summary>
public sealed record ReportQueryResult(IReadOnlyList<ReportQueryRow> Rows, long ElapsedMilliseconds);

public sealed record ReportQueryRow(ReportKey? Key, ReportKey? Series, IReadOnlyList<double?> Values);

/// <param name="Value">Invariant text of the value; the first day for date buckets, null for empty values.</param>
/// <param name="Filter">OData filter that selects the rows of this key, e.g. for cross filters and drill-down.</param>
public sealed record ReportKey(string? Value, string Label, string Filter);

/// <summary>An entity set or app source reports can use, with the fields the signed-in user may group, aggregate and filter by.</summary>
/// <param name="Topic">Realtime topic announcing changes of its rows, if the entity is [Realtime].</param>
/// <param name="Computed">A source the app computes (no OData set): its rows come from <c>POST /api/v1/reports/rows</c>, it has no facets.</param>
public sealed record ReportSourceDto(string EntitySet, string Label, IReadOnlyList<ReportFieldDto> Fields, string? Topic = null, bool Computed = false);

/// <summary>A page of rows of an app source, with the chosen fields only.</summary>
public sealed record ReportRowsRequest(string EntitySet, IReadOnlyList<string> Columns, string? Filter = null, string? OrderBy = null, bool Descending = false, int Skip = 0, int Top = 50);

/// <param name="Rows">One object per row, field path to value (enums by name, dates ISO).</param>
public sealed record ReportRowsResult(IReadOnlyList<System.Text.Json.JsonElement> Rows, long Count);

/// <param name="Path">OData path, e.g. "Status" or "Customer/Name".</param>
/// <param name="EnumType">Qualified OData name of enum fields.</param>
/// <param name="DateOnly">A date without time, filtered with literals like 2026-03-01.</param>
/// <param name="MemberLabels">Display names of <paramref name="Members"/>, in the same order.</param>
public sealed record ReportFieldDto(
    string Path, string Label, ReportFieldKind Kind, IReadOnlyList<string>? Members = null, string? EnumType = null, bool DateOnly = false, IReadOnlyList<string>? MemberLabels = null)
{
    public string MemberLabel(string member) =>
        Members?.ToList().IndexOf(member) is >= 0 and var i && MemberLabels is { } labels && i < labels.Count ? labels[i] : member;
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportFieldKind>))]
public enum ReportFieldKind
{
    Text,
    Integer,
    Number,
    Date,
    Boolean,
    Enum,
    Guid,
}
