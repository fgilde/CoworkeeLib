using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.OData;

namespace Coworkee.Reporting.Querying;

/// <summary>Pages of an app source's rows for table widgets; entity sets page through OData instead.</summary>
internal sealed class ReportRows(IServiceProvider services, ReportQueries queries) : IHandler<ReadReportRowsQuery, Result<ReportRowsResult>>
{
    private static readonly MethodInfo PageMethod = typeof(ReportRows).GetMethod(nameof(PageAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<Result<ReportRowsResult>> HandleAsync(ReadReportRowsQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var found = await queries.ReadableAsync(request.EntitySet, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        if (found.Value is not { Computed: { } computed } source)
        {
            return Error.Validation(nameof(request.EntitySet), $"{request.EntitySet} is an OData set; read its rows there.");
        }

        var granted = await queries.GrantedAsync(cancellationToken);
        if (request.Columns.Append(request.OrderBy).OfType<string>().FirstOrDefault(c => source.Find(c)?.Allowed(granted) != true) is { } unknown)
        {
            return Error.Validation(nameof(request.Columns), $"{source.EntitySet} has no field '{unknown}'.");
        }

        var columns = request.Columns.Select(c => source.Find(c)!).ToList();
        var order = request.OrderBy is null ? null : source.Find(request.OrderBy);
        if (request.Skip < 0 || request.Top is < 1 or > ReportPlan.MaxTop)
        {
            return Error.Validation(nameof(request.Top), $"Top is between 1 and {ReportPlan.MaxTop}, skip not negative.");
        }

        try
        {
            var rows = await computed.RowsAsync(services, request.Filter, cancellationToken);
            return await (Task<ReportRowsResult>)PageMethod.MakeGenericMethod(source.RowType)
                .Invoke(null, [rows, columns, order, request, cancellationToken])!;
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(ReportRowsRequest.Filter), exception.Message);
        }
    }

    private static async Task<ReportRowsResult> PageAsync<TRow>(IQueryable<TRow> rows, List<ReportField> columns, ReportField? order, ReportRowsRequest request, CancellationToken cancellationToken)
    {
        var database = rows.Provider is IAsyncQueryProvider;
        var count = database ? await rows.LongCountAsync(cancellationToken) : rows.LongCount();
        if (order is not null)
        {
            var row = Expression.Parameter(typeof(TRow), "r");
            var key = Expression.Lambda(order.Chain.Aggregate((Expression)row, Expression.Property), row);
            rows = (IQueryable<TRow>)rows.Provider.CreateQuery(Expression.Call(typeof(Queryable), request.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy),
                [typeof(TRow), key.ReturnType], rows.Expression, Expression.Quote(key)));
        }

        var page = rows.Skip(request.Skip).Take(request.Top);
        var items = database ? await page.ToListAsync(cancellationToken) : [.. page];
        return new ReportRowsResult([.. items.Select(item => JsonSerializer.SerializeToElement(columns.ToDictionary(c => c.Path, c => Value(c, item)), ReportHandlers.Json))], count);
    }

    private static object? Value(ReportField field, object? row)
    {
        var value = field.Chain.Aggregate(row, (current, property) => current is null ? null : property.GetValue(current));
        return value is Enum member ? member.ToString() : value;
    }
}
