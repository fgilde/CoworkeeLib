using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Permissions;

internal sealed class ResourceAccessReader(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions) : IResourceAccessReader
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPrincipalsAsync(
        string permission, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken)
    {
        var ids = resourceIds.Distinct().ToList();
        var grants = await db.Set<ResourcePermission>().AsNoTracking().Where(p => p.ResourceType == resourceType && ids.Contains(p.ResourceId))
            .Select(p => new { p.ResourceId, p.PrincipalType, p.PrincipalId, p.RoleId }).ToListAsync(cancellationToken);
        var roles = await RolesGrantingAsync(permission, grants.Select(g => g.RoleId).Distinct().ToList(), cancellationToken);
        var activeUsers = await ActiveUsersAsync(grants.Where(g => g.PrincipalType == PrincipalType.User).Select(g => g.PrincipalId).Distinct().ToList(), cancellationToken);
        return grants.Where(g => roles.Contains(g.RoleId) && (g.PrincipalType != PrincipalType.User || activeUsers.Contains(g.PrincipalId)))
            .GroupBy(g => g.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.PrincipalType == PrincipalType.User ? PrincipalKeys.User(p.PrincipalId) : PrincipalKeys.Group(p.PrincipalId)).Distinct().ToList());
    }

    public async Task<IReadOnlyList<string>> GetCurrentPrincipalsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || (await ActiveUsersAsync([userId], cancellationToken)).Count == 0)
        {
            return [];
        }

        var groups = await (from member in db.Set<UserGroupMember>()
                            join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
                            where member.UserId == userId
                            select grp.Id).ToListAsync(cancellationToken);
        return [PrincipalKeys.User(userId), .. groups.Select(PrincipalKeys.Group)];
    }

    private async Task<HashSet<Guid>> RolesGrantingAsync(string permission, List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var allowed = (await db.Set<Role>().Where(r => roleIds.Contains(r.Id) && r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).ToListAsync(cancellationToken)).ToHashSet();
        var tenantId = currentUser.TenantId;
        var grants = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey) && (g.TenantId == null || g.TenantId == tenantId))
            .Select(g => new { g.ProviderKey, g.Name }).ToListAsync(cancellationToken);
        foreach (var role in grants.GroupBy(g => g.ProviderKey))
        {
            if (definitions.Expand(role.Select(g => g.Name)).Contains(permission))
            {
                allowed.Add(role.Key);
            }
        }

        return allowed;
    }

    private async Task<HashSet<Guid>> ActiveUsersAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        return (await db.Set<User>().Where(u => userIds.Contains(u.Id) && u.TenantId == tenantId && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken)).ToHashSet();
    }
}

/// <summary>Turns grant, role and membership changes into outbox events in the same transaction.</summary>
internal sealed class AccessChangeInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> RuleTypes = [typeof(PermissionGrant), typeof(IdentityUserRole<Guid>), typeof(UserGroupRole), typeof(UserGroupMember), typeof(Role), typeof(UserGroup)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var changed = context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var resources = changed.Select(e => e.Entity).OfType<ResourcePermission>().Select(p => (p.ResourceType, p.ResourceId, p.TenantId)).Distinct().ToList();
        var rules = changed.Any(e => RuleTypes.Contains(e.Metadata.ClrType) || (e.Entity is User && e.State == EntityState.Modified && e.Property(nameof(User.IsActive)).IsModified));
        foreach (var (type, id, tenant) in resources)
        {
            context.Set<OutboxMessage>().Add(Message(new ResourceAccessChanged(type, id), tenant));
        }

        if (rules)
        {
            context.Set<OutboxMessage>().Add(Message(new AccessRulesChanged(), currentUser.TenantId));
        }
    }

    private OutboxMessage Message<T>(T domainEvent, Guid? tenantId)
        where T : notnull => new()
    {
        Type = typeof(T).AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(domainEvent),
        OccurredAt = clock.GetUtcNow(),
        TenantId = tenantId,
        ActorId = currentUser.UserId,
    };
}
