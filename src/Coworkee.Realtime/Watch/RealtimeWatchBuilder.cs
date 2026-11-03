using System.Linq.Expressions;

namespace Coworkee.Realtime.Watch;

/// <summary>How a watch finds what changed in a set another system writes: the date columns, how often it looks and which rows it reads.</summary>
public sealed class RealtimeWatchBuilder<T>
    where T : class
{
    internal LambdaExpression? Created { get; private set; }

    internal List<LambdaExpression> Changed { get; } = [];

    internal TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(30);

    internal int Max { get; private set; } = 200;

    internal TimeSpan LookBackTime { get; private set; }

    internal Func<IQueryable<T>, IServiceProvider, IQueryable<T>>? Rows { get; private set; }

    /// <summary>When a row was created: a row whose date is newer than the last look is sent as created.</summary>
    public RealtimeWatchBuilder<T> CreatedAt(Expression<Func<T, DateTimeOffset?>> created) => SetCreated(created);

    /// <inheritdoc cref="CreatedAt(Expression{Func{T, DateTimeOffset?}})"/>
    public RealtimeWatchBuilder<T> CreatedAt(Expression<Func<T, DateTime?>> created) => SetCreated(created);

    /// <summary>When a row last changed; several columns may be named, a row counts as changed when any of them is newer than the last look.</summary>
    public RealtimeWatchBuilder<T> ChangedAt(Expression<Func<T, DateTimeOffset?>> changed) => AddChanged(changed);

    /// <inheritdoc cref="ChangedAt(Expression{Func{T, DateTimeOffset?}})"/>
    public RealtimeWatchBuilder<T> ChangedAt(Expression<Func<T, DateTime?>> changed) => AddChanged(changed);

    /// <summary>How often the watch looks while someone follows the set (30 seconds).</summary>
    public RealtimeWatchBuilder<T> Every(TimeSpan interval)
    {
        Interval = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        return this;
    }

    /// <summary>
    /// How far before the newest date seen each look reads again, for rows that arrive later than their date (a job that copies rows up to a run late);
    /// a row is sent once per date. None by default.
    /// </summary>
    public RealtimeWatchBuilder<T> LookBack(TimeSpan lookBack)
    {
        LookBackTime = lookBack >= TimeSpan.Zero ? lookBack : throw new ArgumentOutOfRangeException(nameof(lookBack), "Not negative.");
        return this;
    }

    /// <summary>More changed rows in one look than this (200) send one event for the whole set instead of one per row.</summary>
    public RealtimeWatchBuilder<T> MaxEvents(int max)
    {
        Max = max > 0 ? max : throw new ArgumentOutOfRangeException(nameof(max), "At least one event.");
        return this;
    }

    /// <summary>
    /// The rows the watch reads, e.g. <c>(rows, services) =&gt; rows.IgnoreQueryFilters().Where(...)</c> when the set's filters need a signed-in user.
    /// It reads as the system (no user, no organisation); rows of [IMultiTenant] sets are read across organisations and sent to their own.
    /// </summary>
    public RealtimeWatchBuilder<T> Read(Func<IQueryable<T>, IServiceProvider, IQueryable<T>> rows)
    {
        Rows = rows;
        return this;
    }

    private RealtimeWatchBuilder<T> SetCreated(LambdaExpression created)
    {
        Created = created;
        return this;
    }

    private RealtimeWatchBuilder<T> AddChanged(LambdaExpression changed)
    {
        Changed.Add(changed);
        return this;
    }
}
