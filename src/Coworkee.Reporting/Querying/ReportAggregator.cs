using System.Linq.Expressions;
using System.Reflection;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.Reporting.Querying;

/// <summary>
/// Builds GroupBy and Select from a checked <see cref="ReportPlan"/>, so the database groups and aggregates; only the groups come back.
/// The expressions use only fields of the plan, never text from the request.
/// </summary>
internal static class ReportAggregator
{
    public const int MaxGroups = 5000;

    private static readonly MethodInfo FetchMethod = typeof(ReportAggregator).GetMethod(nameof(FetchAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <param name="scope">Narrows navigation fields to the rows their target set shows.</param>
    public static async Task<Result<IReadOnlyList<ReportQueryRow>>> RunAsync<TEntity>(IQueryable<TEntity> rows, ReportPlan plan, CancellationToken cancellationToken, OData.ODataNavigationScope? scope = null)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var (keyBody, keyType) = KeyOf(entity, plan);
        var rowType = typeof(AggregateRow<>).MakeGenericType(keyType);
        var groupType = typeof(IGrouping<,>).MakeGenericType(keyType, typeof(TEntity));

        var grouped = Expression.Call(typeof(Queryable), nameof(Queryable.GroupBy), [typeof(TEntity), keyType], rows.Expression,
            Expression.Quote(Expression.Lambda(keyBody, entity)));
        var group = Expression.Parameter(groupType, "g");
        var bindings = plan.Values.Select((value, i) => (MemberBinding)Expression.Bind(rowType.GetProperty($"V{i}")!, Aggregate(group, typeof(TEntity), value)))
            .Prepend(Expression.Bind(rowType.GetProperty(nameof(AggregateRow<int>.Key))!, Expression.Property(group, nameof(IGrouping<int, int>.Key))));
        Expression query = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [groupType, rowType], grouped,
            Expression.Quote(Expression.Lambda(Expression.MemberInit(Expression.New(rowType), bindings), group)));

        // one dimension: the database sorts and cuts; with series the top applies to categories, which is easier on the fetched groups
        if (plan.Split is null)
        {
            var row = Expression.Parameter(rowType, "r");
            var sortBy = Expression.Lambda(plan.SortBy == ReportSortBy.Value ? Expression.Property(row, "V0") : Expression.Property(row, nameof(AggregateRow<int>.Key)), row);
            query = Expression.Call(typeof(Queryable), plan.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy), [rowType, sortBy.ReturnType],
                query, Expression.Quote(sortBy));
        }

        query = Expression.Call(typeof(Queryable), nameof(Queryable.Take), [rowType], query, Expression.Constant(plan.Split is null && plan.Top is { } top ? top : MaxGroups + 1));
        var built = rows.Provider.CreateQuery(query);
        var fetched = await (Task<List<IAggregateRow>>)FetchMethod.MakeGenericMethod(rowType).Invoke(null, [scope?.Apply(built) ?? built, cancellationToken])!;
        if (fetched.Count > MaxGroups)
        {
            return Error.Validation(nameof(ReportQueryRequest.GroupBy), $"More than {MaxGroups} groups; choose a coarser field or date bucket.");
        }

        var result = fetched.Select(r => ToRow(plan, r, plan.Values.Count)).ToList();
        return plan.Split is null ? result : TopCategories(plan, result);
    }

    private static (Expression Body, Type Type) KeyOf(ParameterExpression entity, ReportPlan plan)
    {
        if (plan.Group is null)
        {
            return (Expression.Constant(1), typeof(int));
        }

        var first = Key(entity, plan.Group);
        if (plan.Split is null)
        {
            return (first, first.Type);
        }

        var second = Key(entity, plan.Split);
        var type = typeof(GroupKey<,>).MakeGenericType(first.Type, second.Type);
        return (Expression.MemberInit(Expression.New(type), Expression.Bind(type.GetProperty("A")!, first), Expression.Bind(type.GetProperty("B")!, second)), type);
    }

    private static Expression Key(ParameterExpression entity, ReportGroup group)
    {
        var access = Access(entity, group.Field);
        return group.Field.Kind == ReportFieldKind.Date ? DateBuckets.Key(access, group.Bucket, group.WeekIndex) : access;
    }

    private static Expression Access(Expression entity, ReportField field) => field.Chain.Aggregate(entity, Expression.Property);

    private static Expression Aggregate(ParameterExpression group, Type entityType, ReportValue value)
    {
        if (value.Function == AggregateFunction.Count)
        {
            return Expression.Convert(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [entityType], group), typeof(double?));
        }

        var entity = Expression.Parameter(entityType, "x");
        var field = Access(entity, value.Field!);
        if (value.Function == AggregateFunction.DistinctCount)
        {
            var selected = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [entityType, field.Type], group, Expression.Lambda(field, entity));
            var distinct = Expression.Call(typeof(Enumerable), nameof(Enumerable.Distinct), [field.Type], selected);
            return Expression.Convert(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [field.Type], distinct), typeof(double?));
        }

        var number = Expression.Lambda(Expression.Convert(field, typeof(double?)), entity);
        var method = value.Function switch
        {
            AggregateFunction.Sum => nameof(Enumerable.Sum),
            AggregateFunction.Average => nameof(Enumerable.Average),
            AggregateFunction.Min => nameof(Enumerable.Min),
            _ => nameof(Enumerable.Max),
        };
        var call = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == method && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1
                && m.GetParameters() is [_, { ParameterType: { IsGenericType: true } selector }] && selector.GetGenericTypeDefinition() == typeof(Func<,>) && selector.GetGenericArguments()[1] == typeof(double?))
            .MakeGenericMethod(entityType);
        return Expression.Call(call, group, number);
    }

    private static ReportQueryRow ToRow(ReportPlan plan, IAggregateRow row, int count)
    {
        var (key, series) = row.Key is IGroupKey pair ? (pair.A, pair.B) : (row.Key, null);
        return new ReportQueryRow(
            plan.Group is null ? null : ReportKeys.For(plan.Group, key),
            plan.Split is null ? null : ReportKeys.For(plan.Split, series),
            row.Values.Take(count).ToList());
    }

    private static List<ReportQueryRow> TopCategories(ReportPlan plan, List<ReportQueryRow> rows)
    {
        var categories = rows.GroupBy(r => r.Key!.Value).Select(g => (Key: g.Key, Total: g.Sum(r => r.Values[0] ?? 0)));
        var ordered = plan.SortBy == ReportSortBy.Value
            ? (plan.Descending ? categories.OrderByDescending(c => c.Total) : categories.OrderBy(c => c.Total))
            : (plan.Descending ? categories.OrderByDescending(c => c.Key, Comparer) : categories.OrderBy(c => c.Key, Comparer));
        var kept = ordered.Take(plan.Top ?? int.MaxValue).Select((c, i) => (c.Key, Index: i)).ToDictionary(c => c.Key ?? string.Empty, c => c.Index);
        return rows.Where(r => kept.ContainsKey(r.Key!.Value ?? string.Empty))
            .OrderBy(r => kept[r.Key!.Value ?? string.Empty])
            .ThenBy(r => r.Series!.Value, Comparer)
            .ToList();
    }

    // numbers, ISO dates and enum names sort well enough as text with numbers by value
    private static readonly IComparer<string?> Comparer = Comparer<string?>.Create((a, b) =>
        double.TryParse(a, System.Globalization.CultureInfo.InvariantCulture, out var x) && double.TryParse(b, System.Globalization.CultureInfo.InvariantCulture, out var y)
            ? x.CompareTo(y)
            : string.CompareOrdinal(a, b));

    private static async Task<List<IAggregateRow>> FetchAsync<TRow>(IQueryable<TRow> query, CancellationToken cancellationToken)
        where TRow : IAggregateRow =>
        query.Provider is IAsyncQueryProvider
            ? [.. (await query.ToListAsync(cancellationToken)).Cast<IAggregateRow>()]
            : [.. query.AsEnumerable().Cast<IAggregateRow>()];
}

internal interface IAggregateRow
{
    object? Key { get; }

    double?[] Values { get; }
}

internal interface IGroupKey
{
    object? A { get; }

    object? B { get; }
}

// a record: in-memory sources group with Enumerable.GroupBy, which needs value equality
internal sealed record GroupKey<TA, TB> : IGroupKey
{
    public TA A { get; init; } = default!;

    public TB B { get; init; } = default!;

    object? IGroupKey.A => A;

    object? IGroupKey.B => B;
}

internal sealed class AggregateRow<TKey> : IAggregateRow
{
    public TKey Key { get; init; } = default!;

    public double? V0 { get; init; }

    public double? V1 { get; init; }

    public double? V2 { get; init; }

    public double? V3 { get; init; }

    public double? V4 { get; init; }

    public double? V5 { get; init; }

    public double? V6 { get; init; }

    public double? V7 { get; init; }

    object? IAggregateRow.Key => Key;

    public double?[] Values => [V0, V1, V2, V3, V4, V5, V6, V7];
}
