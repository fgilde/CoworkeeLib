using System.Globalization;
using System.Linq.Expressions;
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
    private readonly Func<T, DateTimeOffset?>? _createdOf;
    private readonly IReadOnlyList<Func<T, DateTimeOffset?>> _datesOf;
    private readonly TimeSpan _lookBack;

    // the rows sent within the look back, with their date (one watcher reads each watch, so no lock)
    private readonly Dictionary<string, DateTimeOffset?> _sent = new(StringComparer.Ordinal);

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
        _createdOf = _created is null ? null : Compile(_created);
        _datesOf = [.. _columns.Select(Compile)];
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
            _sent.Clear();
            return new RealtimeWatchRound(await NewestAsync(rows, cancellationToken), [], false);
        }

        var from = after - _lookBack;
        var changed = await rows.Where(NewerThan(from)).Take(_max + 1).ToListAsync(cancellationToken);
        if (changed.Count > _max)
        {
            _sent.Clear();
            return new RealtimeWatchRound(await NewestAsync(rows, cancellationToken) ?? after, [], true);
        }

        var key = context.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no key the watch could send.");
        var changes = new List<RealtimeWatchChange>();
        DateTimeOffset? newest = null;
        foreach (var row in changed)
        {
            var id = string.Join(",", key.Select(p => Convert.ToString(p.PropertyInfo?.GetValue(row) ?? p.FieldInfo?.GetValue(row), CultureInfo.InvariantCulture)));
            var date = _datesOf.Select(of => of(row)).Max();
            newest = date > newest || newest is null ? date ?? newest : newest;
            // read again because of the look back: sent already with this date
            var known = _sent.TryGetValue(id, out var sent);
            if (known && sent == date)
            {
                continue;
            }

            _sent[id] = date;
            // new: created since the last look and not sent before (a row the look back finds again, or a later change of it, is an update)
            var created = !known && _createdOf?.Invoke(row) > after;
            changes.Add(new RealtimeWatchChange(id, (row as IMultiTenant)?.TenantId, created ? EntityChangedPayload.Created : EntityChangedPayload.Updated));
        }

        var watermark = newest is { } seen && seen > after ? seen : after;
        foreach (var old in _sent.Where(s => s.Value <= watermark - _lookBack).Select(s => s.Key).ToList())
        {
            _sent.Remove(old);
        }

        return new RealtimeWatchRound(watermark, changes, false);
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

    // any date newer than the watermark: (a > since) or (b > since)
    private Expression<Func<T, bool>> NewerThan(DateTimeOffset since)
    {
        var row = Expression.Parameter(typeof(T), "row");
        var newer = _columns
            .Select(column =>
            {
                var date = new Rebind(column.Parameters[0], row).Visit(column.Body);
                var value = column.ReturnType == typeof(DateTime?) ? Expression.Constant((DateTime?)since.UtcDateTime, typeof(DateTime?)) : Expression.Constant((DateTimeOffset?)since, typeof(DateTimeOffset?));
                return (Expression)Expression.GreaterThan(date, value);
            })
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<T, bool>>(newer, row);
    }

    private static Func<T, DateTimeOffset?> Compile(LambdaExpression column)
    {
        if (column.ReturnType == typeof(DateTime?))
        {
            var date = ((Expression<Func<T, DateTime?>>)column).Compile();
            return row => Utc(date(row));
        }

        return ((Expression<Func<T, DateTimeOffset?>>)column).Compile();
    }

    // dates without an offset are taken as UTC, as EF stores them
    private static DateTimeOffset? Utc(DateTime? date) => date is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
