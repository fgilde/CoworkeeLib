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
}

public interface IResourceHierarchy
{
    string ResourceType { get; }

    Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken);
}
