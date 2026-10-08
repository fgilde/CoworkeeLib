using Coworkee.Contracts.Identity;
using Coworkee.Domain;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

public static class SystemRoles
{
    public const string Admin = "admin";
    public const string User = "user";
}

public sealed class Tenant : AuditedAggregateRoot
{
    public required string Name { get; set; }

    public required string Identifier { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Active")]
    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public bool AcceptsRegistrations { get; set; }
}

[Realtime(IdentityPermissions.Users.View)]
public sealed class User : IdentityUser<Guid>, IAuditable
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid TenantId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Active")]
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Profile picture as data URL.</summary>
    public string? AvatarUrl { get; set; }

    public DateTimeOffset? AvatarChangedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

[Realtime(IdentityPermissions.Roles.View)]
public sealed class Role : IdentityRole<Guid>, IAuditable
{
    public Role() => Id = Guid.CreateVersion7();

    public Guid? TenantId { get; set; }

    public string? Description { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "System")]
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

[Realtime(IdentityPermissions.Groups.View)]
public sealed class UserGroup : AuditedAggregateRoot, IMultiTenant
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid TenantId { get; set; }

    public List<UserGroupMember> Members { get; } = [];

    public List<UserGroupRole> Roles { get; } = [];
}

public sealed class UserGroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }
}

public sealed class UserGroupRole
{
    public Guid GroupId { get; set; }

    public Guid RoleId { get; set; }
}

public sealed class PermissionGrant : AuditedEntity
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public PermissionProviderType ProviderType { get; set; }

    public Guid ProviderKey { get; set; }
}

public sealed class ResourcePermission : AuditedEntity, IMultiTenant
{
    public required string ResourceType { get; set; }

    public Guid ResourceId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    public Guid PrincipalId { get; set; }

    public Guid RoleId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class SystemState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool IsInitialized { get; set; }

    public DateTimeOffset? InitializedAt { get; set; }
}
