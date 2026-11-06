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
