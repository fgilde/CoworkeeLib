namespace Coworkee.Application.Authorization;

/// <summary>On a command or query, or on an application service class or method: the permissions the caller needs.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>On a command or query: refused while the caller is signed in as another user ("Log in as"), e.g. changes of the user's own credentials.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotWhileImpersonatingAttribute : Attribute;

public interface IResourceRequest
{
    string ResourceType { get; }

    Guid ResourceId { get; }

    string RequiredPermission { get; }
}

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

/// <summary>Takes access to single resources away again, for users with certain roles (app rules such as "files with this value are hidden"); applies to every resource check, also for holders of a global grant.</summary>
public interface IResourceRestriction
{
    string ResourceType { get; }

    Task<bool> IsRestrictedAsync(Guid resourceId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
}

public interface IResourceHierarchy
{
    string ResourceType { get; }

    Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken);
}
