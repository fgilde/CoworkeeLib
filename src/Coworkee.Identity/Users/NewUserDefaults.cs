using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Users;

/// <summary>
/// Every way to a new user ends in a save, so this is the one place for the defaults: each added user gets the default roles of its tenant and the
/// global ones, and joins the tenant's default group unless it joins another group in the same save. Roles and groups added in the same save count.
/// It runs first (<see cref="IPreparingInterceptor"/>), so the added rows are audited and published like any other change.
/// </summary>
internal sealed class NewUserDefaults : SaveChangesInterceptor, IPreparingInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Added(eventData.Context) is { Count: > 0 } added)
        {
            var db = eventData.Context!;
            Apply(db, added, tenantId => (DefaultRoles(db, tenantId).ToList(), DefaultGroup(db, tenantId).ToList()));
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Added(eventData.Context) is { Count: > 0 } added)
        {
            var db = eventData.Context!;
            var stored = new Dictionary<Guid, (List<Guid>, List<Guid>)>();
            foreach (var tenantId in added.Select(u => u.TenantId).Distinct())
            {
                stored[tenantId] = (await DefaultRoles(db, tenantId).ToListAsync(cancellationToken), await DefaultGroup(db, tenantId).ToListAsync(cancellationToken));
            }

            Apply(db, added, tenantId => stored[tenantId]);
        }

        return result;
    }

    private static List<User> Added(DbContext? db) =>
        db?.ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList() ?? [];

    private static IQueryable<Guid> DefaultRoles(DbContext db, Guid tenantId) =>
        db.Set<Role>().IgnoreQueryFilters().Where(r => r.IsDefault && !r.IsSystem && (r.TenantId == null || r.TenantId == tenantId)).Select(r => r.Id);

    private static IQueryable<Guid> DefaultGroup(DbContext db, Guid tenantId) =>
        db.Set<UserGroup>().IgnoreQueryFilters().Where(g => g.IsDefault && g.TenantId == tenantId).Select(g => g.Id).Take(1);

    private static void Apply(DbContext db, List<User> added, Func<Guid, (List<Guid> Roles, List<Guid> Group)> stored)
    {
        var tracker = db.ChangeTracker;
        var hasRole = tracker.Entries<IdentityUserRole<Guid>>().Where(e => e.State != EntityState.Deleted).Select(e => (e.Entity.UserId, e.Entity.RoleId)).ToHashSet();
        var joining = tracker.Entries<UserGroupMember>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.UserId).ToHashSet();
        foreach (var tenant in added.GroupBy(u => u.TenantId))
        {
            var tenantId = tenant.Key;
            var (storedRoles, storedGroup) = stored(tenantId);
            var roles = WithTracked(tracker.Entries<Role>(), storedRoles, r => r.IsDefault && !r.IsSystem && (r.TenantId == null || r.TenantId == tenantId), r => r.Id);
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
