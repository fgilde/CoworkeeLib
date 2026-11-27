using System.Globalization;

namespace Coworkee.Contracts.Reporting;

/// <summary>
/// The columns of a grouped table widget in the order of <see cref="ReportTableSort.Column"/>: its group fields, its values, then each join's values.
/// <see cref="WidgetQuery.OrderBy"/> and <see cref="WidgetOptions.HiddenColumns"/> name them: a group field by its path, a value by its label.
/// </summary>
public static class ReportTableColumns
{
    /// <summary>Rows per page of the widget; mails show the first page.</summary>
    public const int PageSize = 20;

    public const int MaxGroupFields = 8;

    /// <param name="Index">The column number of <see cref="ReportTableSort"/>.</param>
    /// <param name="Name">The group field's path, else the value's label (without one e.g. "Count" or "Sum Amount").</param>
    /// <param name="EntitySet">The set the value comes from (the widget's for group fields).</param>
    /// <param name="Field">The group field; null for values.</param>
    /// <param name="Aggregate">The value; null for group fields.</param>
    public sealed record Column(int Index, string Name, string EntitySet, string? Field, ReportAggregate? Aggregate);

    public static IReadOnlyList<Column> Of(WidgetQuery query)
    {
        var columns = query.GroupFields.Select(f => (Name: f, Set: query.EntitySet, Field: (string?)f, Aggregate: (ReportAggregate?)null))
            .Concat(query.Aggregates.Select(a => (Name: Name(a), Set: query.EntitySet, Field: (string?)null, Aggregate: (ReportAggregate?)a)))
            .Concat(query.Joins.SelectMany(j => j.Aggregates.Select(a => (Name: Name(a), Set: j.EntitySet, Field: (string?)null, Aggregate: (ReportAggregate?)a))));
        return [.. columns.Select((c, i) => new Column(i, c.Name, c.Set, c.Field, c.Aggregate))];
    }

    public static string Name(ReportAggregate aggregate) =>
        aggregate.Label is { Length: > 0 } label ? label : aggregate.Function == AggregateFunction.Count ? "Count" : $"{aggregate.Function} {aggregate.Field}";

    public static bool Hidden(Column column, WidgetOptions options) => options.HiddenColumns.Any(name => Names(column, name));

    /// <summary>The entries of <see cref="WidgetQuery.OrderBy"/> as written, e.g. <c>"Total cost desc, Containers"</c>.</summary>
    public static IReadOnlyList<(string Name, bool Descending)> Entries(string? orderBy) =>
        [.. (orderBy ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Entry)];

    public static string? OrderBy(IEnumerable<(string Name, bool Descending)> entries) =>
        string.Join(", ", entries.Where(e => e.Name.Length > 0).Select(e => e.Descending ? $"{e.Name} desc" : e.Name)) is { Length: > 0 } text ? text : null;

    /// <summary>The sorts of <see cref="WidgetQuery.OrderBy"/>; entries naming no column (e.g. of a join the reader may not see) are skipped.</summary>
    public static IReadOnlyList<ReportTableSort> Sorts(string? orderBy, IReadOnlyList<Column> columns)
    {
        var sorts = new List<ReportTableSort>();
        foreach (var (name, descending) in Entries(orderBy))
        {
            if (columns.FirstOrDefault(c => Names(c, name)) is { } column && sorts.TrueForAll(s => s.Column != column.Index))
            {
                sorts.Add(new ReportTableSort(column.Index, descending));
            }
        }

        return sorts;
    }

    /// <summary>A page of the widget's table; <paramref name="filterOf"/> gives the filter of the widget's query and of each join's (<see cref="WidgetJoin.QueryFor"/>).</summary>
    public static ReportTableRequest Request(WidgetQuery query, Func<WidgetQuery, string?> filterOf, IReadOnlyList<ReportTableSort>? orderBy, int skip, int top) =>
        new(query.EntitySet, filterOf(query), query.GroupFields, query.Aggregates,
            [.. query.Joins.Select(j => new ReportTableJoin(j.EntitySet, filterOf(j.QueryFor(query)), j.Aggregates))], orderBy, skip, top);

    /// <summary>A value as the table shows it: counts as whole numbers, other values in the widget's number format, "–" for none.</summary>
    public static string Format(double? value, ReportAggregate aggregate, WidgetOptions options, CultureInfo? culture = null) =>
        ReportNumbers.Format(value, aggregate.Function is AggregateFunction.Count or AggregateFunction.DistinctCount ? Counts : options, culture);

    private static readonly WidgetOptions Counts = new() { Format = NumberFormat.Integer };

    private static bool Names(Column column, string name) => string.Equals(column.Name, name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static (string, bool) Entry(string part)
    {
        var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var last = words.Length > 1 ? words[^1] : null;
        var descending = string.Equals(last, "desc", StringComparison.OrdinalIgnoreCase);
        return descending || string.Equals(last, "asc", StringComparison.OrdinalIgnoreCase) ? (string.Join(' ', words[..^1]), descending) : (string.Join(' ', words), false);
    }
}
