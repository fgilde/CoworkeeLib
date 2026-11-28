using System.Linq.Expressions;
using Coworkee.Contracts.Reporting;
using Coworkee.OData;

namespace Coworkee.Reporting.Querying;

/// <summary>
/// Grouped tables: each source is grouped by all group fields in the database (texts as <c>COALESCE(x, '')</c>), the joins' values are merged onto the
/// main source's groups in memory (texts compare ignoring case, like <c>ISNULL(x, '')</c> under SQL Server's case-insensitive collation), then sorted and paged.
/// </summary>
// ponytail: sources merge in memory, ceiling MaxRows groups per source; a SQL join of sources of the same context if tables outgrow it
public static class ReportTables
{
    public const int MaxGroupFields = ReportTableColumns.MaxGroupFields;

    public const int MaxJoins = 3;

    private const char Separator = '\u001f';

    /// <summary>The most groups a source of a grouped table may have, and the largest page.</summary>
    public static int MaxRows { get; internal set; } = 50_000;

    /// <summary>
    /// The groups of a source with their values, aggregated in the database; null with more than <see cref="MaxRows"/> groups. Text fields group as
    /// <c>COALESCE(x, '')</c>, so null and "" are one group with its values summed and averaged there.
    /// </summary>
    /// <param name="foldText">Groups text fields as <c>UPPER(COALESCE(x, ''))</c>: a join's groups that the merge treats as one key are one group, so its values stay right.</param>
    internal static async Task<List<(object?[] Keys, double?[] Values)>?> GroupAsync<TEntity>(
        IQueryable<TEntity> rows, IReadOnlyList<ReportGroup> groups, IReadOnlyList<ReportValue> values, bool foldText, CancellationToken cancellationToken, ODataNavigationScope? scope = null)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var members = groups.Select(g => ReportAggregator.Key(entity, g)).Select(m => m.Type != typeof(string) ? m : foldText ? Fold(m) : Empty(m)).ToList();
        var types = Enumerable.Range(0, MaxGroupFields).Select(i => i < members.Count ? members[i].Type : typeof(int)).ToArray();
        var keyType = typeof(GroupKey8<,,,,,,,>).MakeGenericType(types);
        var key = Expression.MemberInit(Expression.New(keyType),
            types.Select((_, i) => Expression.Bind(keyType.GetProperty(((char)('A' + i)).ToString())!, i < members.Count ? members[i] : Expression.Constant(0))));

        var (query, rowType) = ReportAggregator.Grouped(rows, entity, key, values);

        // the merge sorts; an order before the limit only keeps the cut defined (and EF from warning about it)
        var row = Expression.Parameter(rowType, "r");
        var first = Expression.Lambda(Expression.Property(Expression.Property(row, nameof(AggregateRow<int>.Key)), "A"), row);
        query = Expression.Call(typeof(Queryable), nameof(Queryable.OrderBy), [rowType, first.ReturnType], query, Expression.Quote(first));
        var built = rows.Provider.CreateQuery(Expression.Call(typeof(Queryable), nameof(Queryable.Take), [rowType], query, Expression.Constant(MaxRows + 1)));
        var fetched = await (Task<List<IAggregateRow>>)ReportAggregator.FetchMethod.MakeGenericMethod(rowType)
            .Invoke(null, [scope is null ? built : await scope.ApplyAsync(built, cancellationToken), cancellationToken])!;
        return fetched.Count > MaxRows ? null : [.. fetched.Select(r => (((GroupKey8)r.Key!).Members[..groups.Count], r.Values[..values.Count]))];
    }

    /// <summary>
    /// The main groups with the joins' values appended (null where a join has no group), sorted by <paramref name="orderBy"/>, then by every key ascending,
    /// last by the keys' exact text. Main groups that are one key ignoring case (Postgres and SQLite group "North" and "north" apart) take a join's values once:
    /// the first by the keys' exact text, the others get null.
    /// </summary>
    internal static (List<ReportTableRow> Rows, bool More) Merge(
        IReadOnlyList<ReportGroup> groups, List<(object?[] Keys, double?[] Values)> main,
        IReadOnlyList<(IReadOnlyList<ReportGroup> Groups, List<(object?[] Keys, double?[] Values)> Rows, int Width)> joins,
        IReadOnlyList<ReportTableSort> orderBy, int skip, int top)
    {
        var lookups = joins.Select(join =>
        {
            var lookup = new Dictionary<string, double?[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in join.Rows)
            {
                // folded in the database already; a provider that folds fewer letters than .NET (SQLite: ASCII only) may leave two, the first wins
                lookup.TryAdd(MergeKey(join.Groups, row.Keys), row.Values);
            }

            return (lookup, join.Width);
        }).ToList();

        var keyed = main.Select(row => (row.Keys, row.Values, Key: MergeKey(groups, row.Keys))).ToList();
        var exact = Comparer<object?[]>.Create((x, y) => CompareExact(groups.Count, x, y));
        var owners = keyed.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Select(g => (object)g.MinBy(r => r.Keys, exact).Keys).ToHashSet(ReferenceEqualityComparer.Instance);
        var rows = keyed.Select(row => (row.Keys, Values: row.Values.Concat(lookups.SelectMany(j =>
            owners.Contains(row.Keys) && j.lookup.TryGetValue(row.Key, out var found) ? found : new double?[j.Width])).ToArray())).ToList();

        var sorts = orderBy.Concat(groups.Select((_, i) => new ReportTableSort(i))).ToList();
        var ordered = rows.Order(Comparer<(object?[] Keys, double?[] Values)>.Create((x, y) =>
        {
            foreach (var sort in sorts)
            {
                var result = sort.Column < groups.Count ? Compare(x.Keys[sort.Column], y.Keys[sort.Column]) : Compare(x.Values[sort.Column - groups.Count], y.Values[sort.Column - groups.Count]);
                if (result != 0)
                {
                    return sort.Descending ? -result : result;
                }
            }

            // keys alike ignoring case (Postgres groups "North" and "north" apart) still get one order, so pages do not overlap
            return CompareExact(groups.Count, x.Keys, y.Keys);
        }));

        var page = ordered.Skip(skip).Take(top).Select(r => new ReportTableRow([.. groups.Select((g, i) => Key(g, r.Keys[i]))], r.Values)).ToList();
        return (page, rows.Count > (long)skip + top);
    }

    // an empty text stands for null and "" alike: shown as null, its filter selects both
    private static ReportKey Key(ReportGroup group, object? value) =>
        value is null or "" && group.Field.ClrType == typeof(string)
            ? new ReportKey(null, string.Empty, $"({group.Field.Path} eq null or {group.Field.Path} eq '')")
            : ReportKeys.For(group, value);

    private static int CompareExact(int count, object?[] x, object?[] y)
    {
        for (var i = 0; i < count; i++)
        {
            var result = string.CompareOrdinal(x[i] as string, y[i] as string);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    // the invariant text of every key (enums by name, dates as their first day), so fields of different types in two sources still match
    private static string MergeKey(IReadOnlyList<ReportGroup> groups, object?[] keys) =>
        string.Join(Separator, groups.Select((g, i) => ReportKeys.For(g, keys[i]).Value ?? string.Empty));

    // nulls first, texts like a case-insensitive collation
    private static int Compare(object? x, object? y) => (x, y) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string a, string b) => StringComparer.InvariantCultureIgnoreCase.Compare(a, b),
        _ => Comparer<object>.Default.Compare(x, y),
    };

    private static BinaryExpression Empty(Expression text) => Expression.Coalesce(text, Expression.Constant(string.Empty));

    private static MethodCallExpression Fold(Expression text) => Expression.Call(Empty(text), typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!);
}
