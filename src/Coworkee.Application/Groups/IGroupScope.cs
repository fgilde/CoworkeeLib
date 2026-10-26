namespace Coworkee.Application.Groups;

/// <summary>The groups (organisation units) the current user works in; apps filter their rows by <see cref="ActiveAsync"/>.</summary>
public interface IGroupScope
{
    /// <summary>The groups the user is a direct member of.</summary>
    Task<IReadOnlyList<Guid>> MembershipsAsync(CancellationToken ct);

    /// <summary>Memberships and all their descendants; every group of the tenant with <c>Identity.Groups.ScopeAll</c>.</summary>
    Task<IReadOnlyList<Guid>> AccessibleAsync(CancellationToken ct);

    /// <summary>The user's selection where it applies (interactive sign-ins or <see cref="GroupScope.Selection"/>), limited to <see cref="AccessibleAsync"/>.</summary>
    Task<IReadOnlyList<Guid>> SelectedAsync(CancellationToken ct);

    /// <summary>The selected groups and their descendants; <see cref="AccessibleAsync"/> when nothing is selected or inside <see cref="GroupScope.Unnarrowed"/>.</summary>
    Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct);

    /// <summary>Id, parent, code and name of any groups of the tenant.</summary>
    Task<IReadOnlyList<GroupNode>> NodesAsync(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>The ancestors of a group, root first, without the group itself.</summary>
    Task<IReadOnlyList<GroupNode>> AncestorsAsync(Guid id, CancellationToken ct);
}

public sealed record GroupNode(Guid Id, Guid? ParentId, string Code, string Name);
