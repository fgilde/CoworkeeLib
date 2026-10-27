using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Users;

/// <summary>
/// Every way to a new user ends in a save, so this is the one place for the defaults: each added user gets the default roles of its tenant and the
/// global ones, and joins the tenant's default group unless it joins another group in the same save. Roles and groups added in the same save count.
/// </summary>
// ponytail: runs after the audit trail interceptor, so the added role links and memberships are not audit entries of their own; the user's creation is
internal sealed class NewUserDefaults : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(eventData.Context, cancellationToken);
        return result;
    }

    private static async Task ApplyAsync(DbContext? db, CancellationToken cancellationToken)
    {
        var added = db?.ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList() ?? [];
        if (added.Count == 0)
        {
            return;
        }

        var tracker = db!.ChangeTracker;
        var hasRole = tracker.Entries<IdentityUserRole<Guid>>().Where(e => e.State != EntityState.Deleted).Select(e => (e.Entity.UserId, e.Entity.RoleId)).ToHashSet();
        var joining = tracker.Entries<UserGroupMember>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.UserId).ToHashSet();
        foreach (var tenant in added.GroupBy(u => u.TenantId))
        {
            var tenantId = tenant.Key;
            var stored = await db.Set<Role>().IgnoreQueryFilters()
                .Where(r => r.IsDefault && !r.IsSystem && (r.TenantId == null || r.TenantId == tenantId)).Select(r => r.Id).ToListAsync(cancellationToken);
            var roles = WithTracked(tracker.Entries<Role>(), stored, r => r.IsDefault && !r.IsSystem && (r.TenantId == null || r.TenantId == tenantId), r => r.Id);
            var storedGroup = await db.Set<UserGroup>().IgnoreQueryFilters()
                .Where(g => g.IsDefault && g.TenantId == tenantId).Select(g => g.Id).Take(1).ToListAsync(cancellationToken);
            var group = WithTracked(tracker.Entries<UserGroup>(), storedGroup, g => g.IsDefault && g.TenantId == tenantId, g => g.Id).FirstOrDefault();
            foreach (var user in tenant)
            {
                db.AddRange(roles.Where(role => !hasRole.Contains((user.Id, role))).Select(role => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role }));
                if (group != Guid.Empty && !joining.Contains(user.Id))
                {
                    db.Add(new UserGroupMember { GroupId = group, UserId = user.Id });
                }
            }
        }
    }

    // what is stored, corrected by what this save adds, changes or deletes
    private static List<Guid> WithTracked<T>(IEnumerable<EntityEntry<T>> entries, List<Guid> stored, Func<T, bool> wanted, Func<T, Guid> id)
        where T : class
    {
        var tracked = entries.Where(e => e.State != EntityState.Unchanged).ToList();
        var gone = tracked.Where(e => e.State == EntityState.Deleted || !wanted(e.Entity)).Select(e => id(e.Entity));
        var come = tracked.Where(e => e.State != EntityState.Deleted && wanted(e.Entity)).Select(e => id(e.Entity));
        return stored.Except(gone).Union(come).ToList();
    }
}
