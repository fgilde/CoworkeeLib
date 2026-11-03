namespace Coworkee.Application.Authorization;

public interface IResourceHierarchy
{
    string ResourceType { get; }

    Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken);
}
