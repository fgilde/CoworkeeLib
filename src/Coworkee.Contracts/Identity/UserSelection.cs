namespace Coworkee.Contracts.Identity;

/// <summary>
/// Users chosen in a table of the Users set (e.g. <c>CoworkeeUserSelection</c>): with <see cref="AllMatching"/> every user that matches <see cref="Filter"/>
/// (an OData <c>$filter</c> of the Users set, null for all), plus <see cref="Included"/>, minus <see cref="Excluded"/>. The server resolves it with the
/// rights of the caller (the Users set: <c>Identity.Users.View</c>, the organisation, row filters, the group selection), so a big selection travels as a
/// filter, never as thousands of ids. Serialisable: store it, send it, resolve it later.
/// </summary>
public sealed record UserSelection(string? Filter = null, bool AllMatching = true, IReadOnlyList<Guid>? Included = null, IReadOnlyList<Guid>? Excluded = null)
{
    /// <summary>The most ids <see cref="Included"/> and <see cref="Excluded"/> take each; more is a filter's job.</summary>
    public const int MaxIds = 1000;

    /// <summary>Only these users (those the caller may see).</summary>
    public static UserSelection Of(params IEnumerable<Guid> ids) => new(null, false, [.. ids]);

    /// <summary>Nobody: no filter match asked for and nobody added.</summary>
    public bool IsEmpty => !AllMatching && Included is not { Count: > 0 };
}

/// <summary>How many users a <see cref="UserSelection"/> reaches: <see cref="Users"/> chosen, <see cref="Active"/> of them may sign in and receive notifications.</summary>
public sealed record UserSelectionCountDto(int Users, int Active);
