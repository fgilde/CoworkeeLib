namespace Coworkee.Application.Authorization;

/// <summary>Takes access to single resources away again, for users with certain roles (app rules such as "files with this value are hidden"); applies to every resource check, also for holders of a global grant.</summary>
public interface IResourceRestriction
{
    string ResourceType { get; }

    Task<bool> IsRestrictedAsync(Guid resourceId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
}
