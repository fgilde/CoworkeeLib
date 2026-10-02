namespace Coworkee.Application.Authorization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

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
