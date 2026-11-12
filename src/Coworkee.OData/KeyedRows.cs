using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData;

/// <summary>
/// Reads a set by its keys: the keys under every filter first, then the rows whose key is among them, with every filter still on. A query that
/// combines a costly row filter (an organisation scope with an <c>EXISTS</c>) with a condition no index serves gets a plan that scans the whole
/// table and tests the filter row by row; by key the database starts from the list. On SQL Server, 282 of 19,007 visible shipments filtered by
/// their pickup date took 39 s to count and 66 s for a page of 25; their keys took 0.8 s, count and page by key 0.15 s each.
/// </summary>
internal static class KeyedRows
{
    // marks a query narrowed by key: narrowed again, the database would probe the second list once for every row of the first
    private const string Tag = "coworkee: keyed rows";

    private static readonly MethodInfo ByKeysMethod = typeof(KeyedRows).GetMethod(nameof(ByKeysOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The set the query starts from has one key of its own, so <see cref="ByKeysAsync{T}"/> can read it.</summary>
    public static bool Readable<T>(IQueryable<T> rows) => Key(rows) is not null;

    /// <summary>
    /// <paramref name="rows"/> narrowed to the keys they have now, and how many there are; null for a set without one key of its own. A query
    /// narrowed already comes back as it is (count -1). No <c>TOP</c>: the caller knows the size, and a <c>TOP</c> of more rows than match is a row
    /// goal that may scan the whole table.
    /// </summary>
    public static Task<(IQueryable<T> Rows, int Count)?> ByKeysAsync<T>(IQueryable<T> rows, CancellationToken cancellationToken)
        where T : class =>
        Keyed(rows.Expression) ? Task.FromResult<(IQueryable<T>, int)?>((rows, -1))
        : Key(rows) is { } key ? (Task<(IQueryable<T>, int)?>)ByKeysMethod.MakeGenericMethod(typeof(T), key.PropertyType).Invoke(null, [rows, key, cancellationToken])!
        : Task.FromResult<(IQueryable<T>, int)?>(null);

    private static async Task<(IQueryable<T>, int)?> ByKeysOfAsync<T, TKey>(IQueryable<T> rows, PropertyInfo key, CancellationToken cancellationToken)
        where T : class
    {
        var it = Expression.Parameter(typeof(T), "it");
        var keys = await rows.Select(Expression.Lambda<Func<T, TKey>>(Expression.Property(it, key), it)).ToListAsync(cancellationToken);
        // one list parameter whatever its length (OPENJSON on SQL Server, json_each on SQLite, an array on PostgreSQL)
        return (rows.Where(row => EF.Parameter(keys).Contains(EF.Property<TKey>(row, key.Name))).TagWith(Tag), keys.Count);
    }

    private static readonly MethodInfo PageMethod = typeof(KeyedRows).GetMethod(nameof(PageOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// A page of <paramref name="ordered"/> (<paramref name="rows"/> in an order, or none) and how many rows there are, without a count: the keys of the
    /// rows in their order first (one read), then the page's rows among them with every filter of <paramref name="rows"/> still on. The keys stop at
    /// <paramref name="limit"/> + 1: above the limit the total is null, and so is the page when it lies beyond those keys. Null for a set without one
    /// key of its own. A filter no index serves then reads the table once instead of once for the count and once more for the page.
    /// </summary>
    public static Task<(List<T>? Page, int? Total)?> PageAsync<T>(IQueryable<T> rows, IQueryable<T> ordered, int skip, int top, int limit, CancellationToken cancellationToken)
        where T : class =>
        !Keyed(rows.Expression) && Key(rows) is { } key
            ? (Task<(List<T>?, int?)?>)PageMethod.MakeGenericMethod(typeof(T), key.PropertyType).Invoke(null, [rows, ordered, key, skip, top, limit, cancellationToken])!
            : Task.FromResult<(List<T>?, int?)?>(null);

    private static async Task<(List<T>?, int?)?> PageOfAsync<T, TKey>(
        IQueryable<T> rows, IQueryable<T> ordered, PropertyInfo key, int skip, int top, int limit, CancellationToken cancellationToken)
        where T : class
        where TKey : notnull
    {
        var it = Expression.Parameter(typeof(T), "it");
        var all = await ordered.Select(Expression.Lambda<Func<T, TKey>>(Expression.Property(it, key), it))
            .Take((int)Math.Min(limit + 1L, int.MaxValue)).ToListAsync(cancellationToken);
        int? total = all.Count <= limit ? all.Count : null;
        if (total is null && (long)skip + top > all.Count)
        {
            return (null, null);
        }

        var keys = all.Skip(skip).Take(top).ToList();
        if (keys.Count == 0)
        {
            return ([], total);
        }

        var page = await rows.Where(row => EF.Parameter(keys).Contains(EF.Property<TKey>(row, key.Name))).TagWith(Tag).ToListAsync(cancellationToken);
        var at = keys.Select((k, i) => (k, i)).ToDictionary(p => p.k, p => p.i);
        return ([.. page.OrderBy(row => at[(TKey)key.GetValue(row)!])], total);
    }

    // the one key property of the set the query starts from
    private static PropertyInfo? Key<T>(IQueryable<T> rows) =>
        Root(rows.Expression) is { } root && root.EntityType.ClrType == typeof(T) && root.EntityType.FindPrimaryKey() is { Properties: [{ PropertyInfo: { } key }] }
            ? key
            : null;

    private static bool Keyed(Expression expression) => expression is MethodCallExpression call
        && ((call.Method.Name == nameof(EntityFrameworkQueryableExtensions.TagWith) && call.Arguments[1] is ConstantExpression { Value: Tag })
            || (call.Arguments.Count > 0 && Keyed(call.Arguments[0])));

    private static EntityQueryRootExpression? Root(Expression expression) => expression switch
    {
        EntityQueryRootExpression root => root,
        MethodCallExpression { Arguments.Count: > 0 } call => Root(call.Arguments[0]),
        _ => null,
    };
}
