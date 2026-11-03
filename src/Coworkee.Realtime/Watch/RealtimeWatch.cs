using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Coworkee.Contracts.Realtime;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Realtime.Watch;

/// <summary>
/// Reads the rows of <typeparamref name="T"/> whose dates are newer than the last look, from the DbContext the type belongs to: one query per look,
/// whoever follows the set. Dates are compared with the dates in the rows (the watermark is the newest date seen), so the clocks of the app and of the
/// system writing them need not agree.
/// </summary>
internal sealed class RealtimeWatch<T> : IRealtimeWatch
    where T : class
{
    private readonly LambdaExpression? _created;
    private readonly IReadOnlyList<LambdaExpression> _columns;
    private readonly int _max;
    private readonly Func<IQueryable<T>, IServiceProvider, IQueryable<T>>? _rows;
    private readonly TimeSpan _lookBack;

    // the rows sent within the look back, with their date (one watcher reads each watch, so no lock)
    private readonly Dictionary<string, DateTimeOffset?> _sent = new(StringComparer.Ordinal);

    private DateTimeOffset? _floor;

    public RealtimeWatch(RealtimeWatchBuilder<T> builder)
    {
        _created = builder.Created;
        _columns = [.. builder.Created is { } created ? [created] : Array.Empty<LambdaExpression>(), .. builder.Changed];
        if (_columns.Count == 0)
        {
            throw new InvalidOperationException($"The watch of {typeof(T).Name} names no date: call CreatedAt or ChangedAt.");
        }

        Interval = builder.Interval;
        _max = builder.Max;
        _rows = builder.Rows;
        _lookBack = builder.LookBackTime;
    }

    public Type EntityType => typeof(T);

    public TimeSpan Interval { get; }

    public async Task<RealtimeWatchRound> ReadAsync(IServiceProvider services, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var context = services.DbContextFor(typeof(T));
        IQueryable<T> rows = context.Set<T>().AsNoTracking();
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(T)))
        {
            // the system reads every organisation's rows; each change goes to its own organisation
            rows = rows.IgnoreQueryFilters();
        }

        rows = _rows?.Invoke(rows, services) ?? rows;
        if (since is not { } after)
        {
            return Restart(await NewestAsync(rows, cancellationToken), many: false);
        }

        // the look back never reads below the first look or the last whole-set event: what was there then counts as sent
        // an empty set's watermark is the smallest date: looking back from it would leave the range of dates
        var from = after - DateTimeOffset.MinValue > _lookBack ? after - _lookBack : DateTimeOffset.MinValue;
        from = _floor > from ? _floor.Value : from;
        var key = context.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no key the watch could send.");
        // the rows sent already come again within the look back: they do not count against the most events of a look
        var changed = await rows.Where(NewerThan(from)).Take(_max + _sent.Count + 1).Select(Shape(key)).ToListAsync(cancellationToken);
        var changes = new List<RealtimeWatchChange>();
        DateTimeOffset? newest = null;
        foreach (var row in changed)
        {
            var id = string.Join(",", row.Key.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
            var date = row.Dates.Max();
            newest = date > newest || newest is null ? date ?? newest : newest;
            // read again because of the look back: sent already with this date
            var known = _sent.TryGetValue(id, out var sent);
            if (known && sent == date)
            {
                continue;
            }

            _sent[id] = date;
            // new: created since the last look and not sent before (a row the look back finds again, or a later change of it, is an update)
            var created = !known && row.Created > after;
            changes.Add(new RealtimeWatchChange(id, row.TenantId, created ? EntityChangedPayload.Created : EntityChangedPayload.Updated));
        }

        if (changes.Count > _max)
        {
            var why = $"{changes.Count} changed rows after {from:O} (last look {after:O}), at most {_max} are sent one by one";
            return Restart(await NewestAsync(rows, cancellationToken) ?? after, many: true) with { Why = why };
        }

        var watermark = newest is { } seen && seen > after ? seen : after;
        foreach (var old in _sent.Where(s => s.Value <= watermark - _lookBack).Select(s => s.Key).ToList())
        {
            _sent.Remove(old);
        }

        return new RealtimeWatchRound(watermark, changes, false);
    }

    // the first look, or one with more changes than events: everything up to the newest date counts as known
    private RealtimeWatchRound Restart(DateTimeOffset? newest, bool many)
    {
        _sent.Clear();
        _floor = newest;
        return new RealtimeWatchRound(newest, [], many);
    }

    private async Task<DateTimeOffset?> NewestAsync(IQueryable<T> rows, CancellationToken cancellationToken)
    {
        DateTimeOffset? newest = null;
        foreach (var column in _columns)
        {
            var max = column.ReturnType == typeof(DateTime?)
                ? Utc(await rows.MaxAsync((Expression<Func<T, DateTime?>>)column, cancellationToken))
                : await rows.MaxAsync((Expression<Func<T, DateTimeOffset?>>)column, cancellationToken);
            if (max > newest || newest is null)
            {
                newest = max ?? newest;
            }
        }

        return newest;
    }

    // any date newer than the watermark: (a > since) or (b > since); the watermark is read from a closure, so EF sends it as a parameter (one plan for every look)
    private Expression<Func<T, bool>> NewerThan(DateTimeOffset since)
    {
        var row = Expression.Parameter(typeof(T), "row");
        var watermark = new Watermark((DateTimeOffset?)since, (DateTime?)since.UtcDateTime);
        var newer = _columns
            .Select(column =>
            {
                var date = new Rebind(column.Parameters[0], row).Visit(column.Body);
                var value = Expression.Property(Expression.Constant(watermark), column.ReturnType == typeof(DateTime?) ? nameof(Watermark.Utc) : nameof(Watermark.Offset));
                return (Expression)Expression.GreaterThan(date, value);
            })
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<T, bool>>(newer, row);
    }

    // row => new RealtimeWatchRow(new object?[] { key... }, tenant, new DateTimeOffset?[] { dates... }, created): only these columns are read
    private Expression<Func<T, RealtimeWatchRow>> Shape(IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IProperty> key)
    {
        var row = Expression.Parameter(typeof(T), "row");
        var keys = key.Select(p => (Expression)Expression.Convert(Column(row, p.ClrType, p.Name), typeof(object)));
        var tenant = typeof(IMultiTenant).IsAssignableFrom(typeof(T))
            ? (Expression)Expression.Convert(Column(row, typeof(Guid), nameof(IMultiTenant.TenantId)), typeof(Guid?))
            : Expression.Constant(null, typeof(Guid?));
        var dates = _columns.Select(column => Date(column, row));
        var created = _created is null ? Expression.Constant(null, typeof(DateTimeOffset?)) : Date(_created, row);
        return Expression.Lambda<Func<T, RealtimeWatchRow>>(
            Expression.New(
                typeof(RealtimeWatchRow).GetConstructors()[0],
                Expression.NewArrayInit(typeof(object), keys),
                tenant,
                Expression.NewArrayInit(typeof(DateTimeOffset?), dates),
                created),
            row);
    }

    private static Expression Column(ParameterExpression row, Type type, string name) =>
        Expression.Call(typeof(EF), nameof(EF.Property), [type], row, Expression.Constant(name));

    private static Expression Date(LambdaExpression column, ParameterExpression row)
    {
        var date = new Rebind(column.Parameters[0], row).Visit(column.Body);
        return column.ReturnType == typeof(DateTime?) ? Expression.Call(typeof(RealtimeWatch<T>).GetMethod(nameof(Utc), BindingFlags.NonPublic | BindingFlags.Static)!, date) : date;
    }

    // dates without an offset are taken as UTC, as EF stores them
    private static DateTimeOffset? Utc(DateTime? date) => date is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;

    private sealed record Watermark(DateTimeOffset? Offset, DateTime? Utc);

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
