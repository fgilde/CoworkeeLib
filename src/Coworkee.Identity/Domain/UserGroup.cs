using Coworkee.Contracts.Identity;
using Coworkee.Domain;

namespace Coworkee.Identity.Domain;

[Realtime(IdentityPermissions.Groups.View)]
public sealed class UserGroup : AuditedAggregateRoot, IMultiTenant
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid TenantId { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>Path of 5-digit segments joined by dots (<c>00001.00002</c>); null only until the migrations host has filled it for groups that existed before the tree.</summary>
    public string? Code { get; set; }

    /// <summary>The default group of the tenant (at most one): new users join it unless they join another group as they are created.</summary>
    public bool IsDefault { get; set; }

    public List<UserGroupMember> Members { get; } = [];

    public List<UserGroupRole> Roles { get; } = [];
}
