namespace Coworkee.Application.Events;

/// <summary>
/// A user as <see cref="EventTarget.Users(System.Linq.Expressions.Expression{Func{EventUser, bool}})"/> sees him: only for predicates, which run in the
/// database. The markers stand for subqueries over the library's roles, grants and groups; calling them outside a predicate throws.
/// </summary>
public sealed class EventUser
{
    private EventUser()
    {
    }

    public Guid Id { get; }

    public string? UserName { get; }

    public string? Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }

    /// <summary>Holds the permission (or one that implies it) through a role, given directly or through a group, a grant of his own or of one of his groups.</summary>
    public bool HasPermission(string permission) => throw Marker(permission);

    /// <summary>Has the role of this name, directly or through one of his groups.</summary>
    public bool IsInRole(string role) => throw Marker(role);

    /// <summary>Is a member of the group or of one of its sub-groups.</summary>
    public bool InGroup(Guid groupId) => throw Marker(groupId);

    /// <summary>Has a realtime connection now.</summary>
    public bool IsOnline() => throw Marker(null);

    private static NotSupportedException Marker(object? argument) =>
        new($"EventUser markers run in the database only, inside an EventTarget predicate ({argument}).");
}
