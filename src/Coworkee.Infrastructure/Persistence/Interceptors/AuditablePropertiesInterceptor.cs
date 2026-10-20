using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class AuditablePropertiesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
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
        var now = clock.GetUtcNow();
        foreach (var entry in context?.ChangeTracker.Entries<IAuditable>() ?? [])
        {
            var anonymous = entry.Entity as IAnonymous;
            var at = anonymous?.Truncate(now) ?? now;
            var by = anonymous is null ? currentUser.UserId : null;
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = at;
                entry.Entity.CreatedBy = by;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = at;
                entry.Entity.ModifiedBy = by;
            }
        }

        if (context is not null && !context.Database.IsNpgsql())
        {
            foreach (var entry in context.ChangeTracker.Entries<IHasConcurrencyToken>().Where(e => e.State == EntityState.Modified))
            {
                entry.Entity.Version++;
            }
        }
    }
}
