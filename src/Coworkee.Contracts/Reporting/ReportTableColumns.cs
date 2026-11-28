using System.Globalization;

namespace Coworkee.Contracts.Reporting;

/// <summary>
/// The columns of a grouped table widget in the order of <see cref="ReportTableSort.Column"/>: its group fields, its values, then each join's values.
/// <see cref="WidgetQuery.OrderBy"/> and <see cref="WidgetOptions.HiddenColumns"/> name them: a group field by its path, a value by its label; names are unique
/// (<see cref="Clashes"/>).
/// </summary>
public static class ReportTableColumns
{
    /// <summary>Rows per page of the widget; mails show the first page.</summary>
    public const int PageSize = 20;

    public const int MaxGroupFields = 8;

    /// <param name="Index">The column number of <see cref="ReportTableSort"/>.</param>
    /// <param name="Name">The group field's path, else the value's label (without one e.g. "Count" or "Sum Amount", a join's with its set: "Count (Containers)").</param>
    /// <param name="EntitySet">The set the value comes from (the widget's for group fields).</param>
    /// <param name="Field">The group field; null for values.</param>
    /// <param name="Aggregate">The value; null for group fields.</param>
    /// <param name="Key">Where the column is defined in the query, e.g. <c>GroupFields[0]</c>, <c>Aggregates[1]</c>, <c>Joins[0].Aggregates[0]</c>.</param>
    /// <param name="Join">The join the value comes from; null for group fields and the widget's own values.</param>
    public sealed record Column(int Index, string Name, string EntitySet, string? Field, ReportAggregate? Aggregate, string Key, int? Join);

    public static IReadOnlyList<Column> Of(WidgetQuery query)
    {
        var columns = query.GroupFields.Select((f, i) => (Name: f, Set: query.EntitySet, Field: (string?)f, Aggregate: (ReportAggregate?)null, Key: $"GroupFields[{i}]", Join: (int?)null))
            .Concat(query.Aggregates.Select((a, i) => (Name: Name(a), Set: query.EntitySet, Field: (string?)null, Aggregate: (ReportAggregate?)a, Key: $"Aggregates[{i}]", Join: (int?)null)))
            .Concat(query.Joins.SelectMany((j, n) => j.Aggregates.Select((a, i) =>
                (Name: Name(a, j.EntitySet), Set: j.EntitySet, Field: (string?)null, Aggregate: (ReportAggregate?)a, Key: $"Joins[{n}].Aggregates[{i}]", Join: (int?)n))));
        return [.. columns.Select((c, i) => new Column(i, c.Name, c.Set, c.Field, c.Aggregate, c.Key, c.Join))];
    }

    /// <param name="joinedSet">The set of a join: a value without a label is named with it, so it does not clash with the widget's own.</param>
    public static string Name(ReportAggregate aggregate, string? joinedSet = null) =>
        aggregate.Label is { Length: > 0 } label ? label
        : (aggregate.Function == AggregateFunction.Count ? "Count" : $"{aggregate.Function} {aggregate.Field}") + (joinedSet is null ? string.Empty : $" ({joinedSet})");

    /// <summary>The columns named like an earlier one (ignoring case); their name must change.</summary>
    public static IReadOnlyList<Column> Clashes(IReadOnlyList<Column> columns) =>
        [.. columns.Where((c, i) => columns.Take(i).Any(earlier => Names(earlier, c.Name)))];

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

    /// <summary>
    /// A value as the table shows it: in its own <see cref="ReportAggregate.Format"/>, else counts as whole numbers and other values in the widget's format, with its
    /// <see cref="ReportAggregate.Unit"/>; "–" for none. <paramref name="currency"/> is the currency of the value's own set (null: the widget's); <paramref name="translate"/> translates the unit.
    /// </summary>
    public static string Format(double? value, ReportAggregate aggregate, WidgetOptions options, CultureInfo? culture = null, string? currency = null, Func<string, string>? translate = null)
    {
        var format = aggregate.Format ?? (aggregate.Function is AggregateFunction.Count or AggregateFunction.DistinctCount ? NumberFormat.Integer : options.Format);
        var text = ReportNumbers.Format(value, new WidgetOptions { Format = format, Currency = string.IsNullOrWhiteSpace(currency) ? options.Currency : currency }, culture);
        return value is null || string.IsNullOrWhiteSpace(aggregate.Unit) ? text : $"{text} {(translate is null ? aggregate.Unit.Trim() : translate(aggregate.Unit.Trim()))}";
    }

    private static bool Names(Column column, string name) => string.Equals(column.Name, name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static (string, bool) Entry(string part)
    {
        var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var last = words.Length > 1 ? words[^1] : null;
        var descending = string.Equals(last, "desc", StringComparison.OrdinalIgnoreCase);
        return descending || string.Equals(last, "asc", StringComparison.OrdinalIgnoreCase) ? (string.Join(' ', words[..^1]), descending) : (string.Join(' ', words), false);
    }
}
