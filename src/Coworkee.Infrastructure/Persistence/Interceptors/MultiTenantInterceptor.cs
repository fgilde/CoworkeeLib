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
        foreach (var entry in context?.ChangeTracker.Entries<IMultiTenant>().Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty) ?? [])
        {
            entry.Entity.TenantId = currentUser.TenantId
                ?? throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} without a tenant.");
        }
    }
}
