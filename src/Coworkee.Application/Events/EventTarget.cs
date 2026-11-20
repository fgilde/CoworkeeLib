using System.Linq.Expressions;

namespace Coworkee.Application.Events;

/// <summary>
/// Who receives something that happens: a toast, a stored notification, a mail, a realtime event. One model for all of them, resolved by
/// <see cref="IEventTargetResolver"/> inside the current organisation. A target only narrows: realtime topics keep their subscribe-time
/// authorization, and every user it names is an active user of the current organisation (except <see cref="Global"/>).
/// </summary>
/// <example>
/// <code>
/// EventTarget.Permission(OrderPermissions.View).Except(EventTarget.Current)
/// EventTarget.Users(u => u.InGroup(salesId) &amp;&amp; u.HasPermission(OrderPermissions.Approve))
/// EventTarget.Users(aliceId, bobId) | EventTarget.Role("Dispatcher")
/// </code>
/// </example>
public abstract record EventTarget
{
    private protected EventTarget()
    {
    }

    /// <summary>Every user of the current organisation; realtime sends it to one group of the organisation, nobody is looked up.</summary>
    public static EventTarget All { get; } = new AllTarget();

    /// <summary>Every user of every organisation: realtime and toasts only (system messages such as maintenance); stored notifications refuse it.</summary>
    public static EventTarget Global { get; } = new GlobalTarget();

    /// <summary>The current session in realtime and toasts (all tabs of this sign-in); the current user everywhere else and inside <see cref="Except"/>.</summary>
    public static EventTarget Current { get; } = new CurrentTarget();

    /// <summary>These users. Stored notifications take them as given (as <c>NotifyAsync</c> always did); realtime reaches those who are online.</summary>
    public static EventTarget Users(params IEnumerable<Guid> ids) => new UsersTarget([.. ids.Distinct()]);

    /// <summary>
    /// The users of the organisation who match <paramref name="predicate"/>. It runs in the database as one query: the members of <see cref="EventUser"/>
    /// and its markers <see cref="EventUser.HasPermission"/>, <see cref="EventUser.IsInRole"/>, <see cref="EventUser.InGroup"/> and
    /// <see cref="EventUser.IsOnline"/>. Anything the database cannot run fails at once with a message naming it.
    /// </summary>
    public static EventTarget Users(Expression<Func<EventUser, bool>> predicate) => new WhereTarget(predicate);

    /// <summary>The users who hold <paramref name="permission"/>, with the semantics of <c>IPermissionChecker</c>.</summary>
    public static EventTarget Permission(string permission) => Users(u => u.HasPermission(permission));

    /// <summary>The users who have the role named <paramref name="role"/>, directly or through a group.</summary>
    public static EventTarget Role(string role) => Users(u => u.IsInRole(role));

    /// <summary>The members of the group and of its sub-groups.</summary>
    public static EventTarget Group(Guid groupId) => Users(u => u.InGroup(groupId));

    /// <summary>The users an <see cref="IAudienceResolver"/> of the app names for <paramref name="args"/>, e.g. the parties of a shipment.</summary>
    public static EventTarget Custom<TResolver>(object? args = null)
        where TResolver : IAudienceResolver => new CustomTarget(typeof(TResolver), args);

    /// <summary>This target without the users of <paramref name="other"/>, e.g. <c>.Except(EventTarget.Current)</c> to leave out the one who acted.</summary>
    public EventTarget Except(EventTarget other) => new ExceptTarget(this, other);

    /// <summary>The users of both targets.</summary>
    public static EventTarget operator |(EventTarget left, EventTarget right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left is GlobalTarget || right is GlobalTarget ? Global
            : left is AllTarget || right is AllTarget ? All
            : new UnionTarget(left, right);
    }

    /// <summary>Alias of <see cref="op_BitwiseOr"/> for languages without operators.</summary>
    public EventTarget Or(EventTarget other) => this | other;

    /// <summary>
    /// The users the target names in memory, without a database: null when a part needs it (<see cref="All"/>, predicates, custom audiences).
    /// <paramref name="among"/> limits the result (e.g. to the online users).
    /// </summary>
    internal abstract HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among);

    /// <summary>True when <paramref name="userId"/> is named by id (or as <see cref="Current"/>) outside an <see cref="Except"/>: he is addressed directly.</summary>
    internal virtual bool Names(Guid userId, Guid? current) => false;

    /// <summary>True when a part needs the database.</summary>
    internal abstract bool NeedsDatabase { get; }
}

internal sealed record AllTarget : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) => null;

    internal override bool NeedsDatabase => true;
}

internal sealed record GlobalTarget : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) =>
        throw new NotSupportedException("EventTarget.Global reaches every organisation and is for realtime and toasts only.");

    internal override bool NeedsDatabase => false;
}

internal sealed record CurrentTarget : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) =>
        current is { } id && (among is null || among.Contains(id)) ? [id] : [];

    internal override bool Names(Guid userId, Guid? current) => userId == current;

    internal override bool NeedsDatabase => false;
}

internal sealed record UsersTarget(IReadOnlyCollection<Guid> Ids) : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) =>
        [.. among is null ? Ids : Ids.Where(among.Contains)];

    internal override bool Names(Guid userId, Guid? current) => Ids.Contains(userId);

    internal override bool NeedsDatabase => false;
}

internal sealed record WhereTarget(Expression<Func<EventUser, bool>> Predicate) : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) => null;

    internal override bool NeedsDatabase => true;
}

internal sealed record CustomTarget(Type Resolver, object? Args) : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) => null;

    internal override bool NeedsDatabase => true;
}

internal sealed record UnionTarget(EventTarget Left, EventTarget Right) : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) =>
        Left.InMemory(current, among) is { } left && Right.InMemory(current, among) is { } right ? [.. left.Union(right)] : null;

    internal override bool Names(Guid userId, Guid? current) => Left.Names(userId, current) || Right.Names(userId, current);

    internal override bool NeedsDatabase => Left.NeedsDatabase || Right.NeedsDatabase;
}

internal sealed record ExceptTarget(EventTarget Target, EventTarget Excluded) : EventTarget
{
    internal override HashSet<Guid>? InMemory(Guid? current, IReadOnlyCollection<Guid>? among) =>
        Target.InMemory(current, among) is { } target && Excluded.InMemory(current, among) is { } excluded ? [.. target.Except(excluded)] : null;

    internal override bool Names(Guid userId, Guid? current) => Target.Names(userId, current) && !Excluded.Names(userId, current);

    internal override bool NeedsDatabase => Target.NeedsDatabase || Excluded.NeedsDatabase;
}
