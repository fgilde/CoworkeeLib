using System.Linq.Expressions;
using System.Reflection;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.Reporting.Querying;

/// <summary>The first rows of a table widget on the server (entity sets with their row filters, or app sources), one value per column.</summary>
internal static class ReportTableRows
{
    private static readonly MethodInfo ReadMethod = typeof(ReportTableRows).GetMethod(nameof(ReadTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task<List<object?[]>> ReadAsync(
        IServiceProvider services, ReportSource source, IReadOnlyList<ReportField> columns, string? filter, ReportField? order, bool descending, int top, CancellationToken cancellationToken) =>
        (Task<List<object?[]>>)ReadMethod.MakeGenericMethod(source.RowType).Invoke(null, [services, source, columns, filter, order, descending, top, cancellationToken])!;

    private static async Task<List<object?[]>> ReadTypedAsync<TRow>(
        IServiceProvider services, ReportSource source, IReadOnlyList<ReportField> columns, string? filter, ReportField? order, bool descending, int top, CancellationToken cancellationToken)
        where TRow : class
    {
        var rows = source.Computed is { } computed
            ? (IQueryable<TRow>)await computed.RowsAsync(services, filter, cancellationToken)
            : await ODataEntityAccess.FilteredAsync<TRow>(services, filter, cancellationToken);
        var row = Expression.Parameter(typeof(TRow), "r");
        if (order is not null)
        {
            var key = Expression.Lambda(order.Chain.Aggregate((Expression)row, Expression.Property), row);
            rows = (IQueryable<TRow>)rows.Provider.CreateQuery(Expression.Call(typeof(Queryable), descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy),
                [typeof(TRow), key.ReturnType], rows.Expression, Expression.Quote(key)));
        }

        var values = Expression.Lambda<Func<TRow, object?[]>>(
            Expression.NewArrayInit(typeof(object), columns.Select(c => Expression.Convert(c.Chain.Aggregate((Expression)row, Expression.Property), typeof(object)))), row);
        var page = rows.Take(top).Select(values);
        return page.Provider is IAsyncQueryProvider ? await page.ToListAsync(cancellationToken) : [.. page];
    }
}
