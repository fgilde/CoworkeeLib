namespace Coworkee.Application.Authorization;

public interface IPermissionChecker
{
    Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken);

    Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken);

    Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> GetGrantedResourcesAsync(string permission, string resourceType, CancellationToken cancellationToken);

    /// <summary>The tenant wide roles of the current user, directly and through groups; for rules an app attaches to roles (such as restrictions).</summary>
    Task<IReadOnlyCollection<Guid>> GetRoleIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<Guid>>([]);

    /// <summary>
    /// Active users of the current user's organisation who hold <paramref name="permission"/> (or a permission that implies it) through a role, given directly
    /// or through a group, a grant of their own or a grant of one of their groups; ordered by email, <see cref="Contracts.PageRequest.Search"/> looks in email
    /// and names. <paramref name="groupId"/> narrows to members of that group and its sub-groups. As for <see cref="IsGrantedAsync(string, CancellationToken)"/>,
    /// a group grants only to its own members, not to those of its sub-groups. All roles count whatever role a user picked as active; computed permissions
    /// and grants on single resources do not.
    /// </summary>
    Task<Contracts.PagedResult<Guid>> UsersGrantedAsync(string permission, Guid? groupId, Contracts.PageRequest page, CancellationToken cancellationToken) =>
        Task.FromResult(new Contracts.PagedResult<Guid>([], 0, page.Page, page.PageSize));
}
