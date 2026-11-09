namespace Coworkee.Identity.Users;

/// <summary><c>Coworkee:Identity</c>: how far identity administration reaches.</summary>
public sealed class IdentityAdministrationOptions
{
    public const string Section = "Coworkee:Identity";

    /// <summary>
    /// Administrators reach only their groups, the sub-groups and the users who are members of one of them (<see cref="AdminReach"/>), unless they hold
    /// <c>Identity.Groups.ScopeAll</c>, are administrators of the organisation or administer every group. Off (the default): every administrator reaches the
    /// whole organisation, as before.
    /// </summary>
    public bool GroupScopedAdministration { get; set; }

    /// <summary>
    /// Role names code looks roles up by (approvers of workflows, a module's own role): renaming such a role is refused, its description stays editable.
    /// The Files module adds its registration documents role, the Workflows module the roles its declared workflows name; apps add theirs.
    /// </summary>
    public HashSet<string> FixedRoleNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}
