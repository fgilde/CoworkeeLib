using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class MultiTenantInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        var tenantId = currentUser.TenantId;
        foreach (var entry in context?.ChangeTracker.Entries<IMultiTenant>().Where(e => e.State is EntityState.Added or EntityState.Modified) ?? [])
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId
                    ?? throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} without a tenant.");
            }
            else if (tenantId is { } mine && entry.Entity.TenantId != mine)
            {
                throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} into a foreign tenant.");
            }
        }
    }
}
