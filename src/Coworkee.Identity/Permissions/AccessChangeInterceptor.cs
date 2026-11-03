using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Permissions;

/// <summary>Turns grant, role and membership changes into outbox events in the same transaction.</summary>
internal sealed class AccessChangeInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{

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
        foreach (var (type, id, tenant) in resources)
        {
            context.Set<OutboxMessage>().Add(Message(new ResourceAccessChanged(type, id), tenant));
        }

        // memberships, role assignments and user states are resolved when reading; only what a role grants changes the meaning of stored grants
        var roles = changed.Where(e => e.Entity is PermissionGrant { ProviderType: PermissionProviderType.Role })
            .Select(e => (Role: ((PermissionGrant)e.Entity).ProviderKey, Tenant: ((PermissionGrant)e.Entity).TenantId))
            .Concat(changed.Where(e => e.Entity is Role && e.State == EntityState.Deleted).Select(e => (Role: ((Role)e.Entity).Id, Tenant: ((Role)e.Entity).TenantId)))
            .ToList();
        foreach (var tenant in roles.GroupBy(r => r.Tenant))
        {
            context.Set<OutboxMessage>().Add(Message(new AccessRulesChanged([.. tenant.Select(r => r.Role).Distinct()]), tenant.Key));
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
        ImpersonatorId = currentUser.ImpersonatorId,
    };
}
