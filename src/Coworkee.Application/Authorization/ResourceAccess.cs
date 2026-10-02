using Coworkee.Domain;

namespace Coworkee.Application.Authorization;

/// <summary>Who may do what on resources, for consumers that materialise access (search indexes).</summary>
public interface IResourceAccessReader
{
    /// <summary>For each resource, the principal keys (see <see cref="PrincipalKeys"/>) holding a role with <paramref name="permission"/> directly on it; inheritance is up to the caller.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPrincipalsAsync(string permission, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken);

    /// <summary>The principal keys of the current user (the user and its groups); empty for anonymous or inactive users.</summary>
    Task<IReadOnlyList<string>> GetCurrentPrincipalsAsync(CancellationToken cancellationToken);
}

public static class PrincipalKeys
{
    public static string User(Guid id) => "u:" + id.ToString("N");

    public static string Group(Guid id) => "g:" + id.ToString("N");
}

/// <summary>Grants on one resource were added, changed or removed.</summary>
public sealed record ResourceAccessChanged(string ResourceType, Guid ResourceId) : IDomainEvent;

/// <summary>Role permissions, group memberships or user states changed; any resource grant may mean something else now.</summary>
public sealed record AccessRulesChanged : IDomainEvent;
