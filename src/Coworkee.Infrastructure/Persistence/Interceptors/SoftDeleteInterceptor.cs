using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class SoftDeleteInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
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
        foreach (var entry in context?.ChangeTracker.Entries<ISoftDelete>().Where(e => e.State == EntityState.Deleted) ?? [])
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            var anonymous = entry.Entity as IAnonymous;
            entry.Entity.DeletedAt = anonymous?.Truncate(clock.GetUtcNow()) ?? clock.GetUtcNow();
            entry.Entity.DeletedBy = anonymous is null ? currentUser.UserId : null;
        }
    }
}
