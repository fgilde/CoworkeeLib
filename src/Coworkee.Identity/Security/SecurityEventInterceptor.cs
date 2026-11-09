using Coworkee.Application.Auditing;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.ApiKeys;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Security;

/// <summary>
/// Writes the security log entries of credential and access changes in the same save, whatever code path made them (own profile, administration, reset
/// link, failed sign-ins): passwords, e-mail, two-factor, lockouts, passkeys, roles and own grants of a user, API keys.
/// </summary>
// ponytail: an algorithm upgrade that rehashes a password at sign-in counts as a password change; tell them apart if hashes are ever upgraded
internal sealed class SecurityEventInterceptor(SecurityLog log, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            context.AddRange(Collect(context, ids => Task.FromResult(RoleNames(context, ids).ToList())).GetAwaiter().GetResult());
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            context.AddRange(await Collect(context, ids => RoleNames(context, ids).ToListAsync(cancellationToken)));
        }

        return result;
    }

    private async Task<List<SecurityEvent>> Collect(DbContext context, Func<List<Guid>, Task<List<RoleName>>> loadRoles)
    {
        var changed = context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var events = new List<SecurityEvent>();
        var users = context.ChangeTracker.Entries<User>().ToDictionary(e => e.Entity.Id, e => (Guid?)e.Entity.TenantId);
        var roleIds = changed.Select(e => e.Entity).OfType<IdentityUserRole<Guid>>().Select(r => r.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await loadRoles(roleIds)).ToDictionary(r => r.Id);
        foreach (var entry in changed)
        {
            var added = entry.State == EntityState.Added;
            switch (entry.Entity)
            {
                case User user when entry.State == EntityState.Modified:
                    events.AddRange(UserChanges((EntityEntry<User>)entry.Context.Entry(user), user));
                    break;
                case IdentityUserPasskey<Guid> passkey when entry.State != EntityState.Modified:
                    events.Add(Event(added ? SecurityActions.PasskeyAdded : SecurityActions.PasskeyRemoved, passkey.UserId, users.GetValueOrDefault(passkey.UserId),
                        new { name = passkey.Data?.Name }));
                    break;
                case IdentityUserRole<Guid> role when entry.State != EntityState.Modified:
                    var known = roles.GetValueOrDefault(role.RoleId);
                    events.Add(Event(added ? SecurityActions.RoleAssigned : SecurityActions.RoleRemoved, role.UserId, users.GetValueOrDefault(role.UserId) ?? known?.TenantId,
                        new { roleId = role.RoleId, role = known?.Name }));
                    break;
                case PermissionGrant { ProviderType: PermissionProviderType.User } grant when entry.State != EntityState.Modified:
                    events.Add(Event(added ? SecurityActions.PermissionGranted : SecurityActions.PermissionRevoked, grant.ProviderKey, grant.TenantId, new { permission = grant.Name }));
                    break;
                case ApiKey key when added:
                    events.Add(Event(SecurityActions.ApiKeyCreated, key.UserId, key.TenantId, new { apiKeyId = key.Id, key.Name, key.Prefix }));
                    break;
                case ApiKey key when entry.Property(nameof(ApiKey.RevokedAt)).OriginalValue is null && key.RevokedAt is not null:
                    events.Add(Event(SecurityActions.ApiKeyRevoked, key.UserId, key.TenantId, new { apiKeyId = key.Id, key.Name, key.Prefix }));
                    break;
            }
        }

        return events;
    }

    private IEnumerable<SecurityEvent> UserChanges(EntityEntry<User> entry, User user)
    {
        if (Changed(entry, u => u.PasswordHash))
        {
            yield return Event(SecurityActions.PasswordChanged, user.Id, user.TenantId, null);
        }

        if (Changed(entry, u => u.Email))
        {
            yield return Event(SecurityActions.EmailChanged, user.Id, user.TenantId, new { from = entry.Property(u => u.Email).OriginalValue, to = user.Email });
        }

        if (Changed(entry, u => u.TwoFactorEnabled))
        {
            yield return Event(user.TwoFactorEnabled ? SecurityActions.TwoFactorEnabled : SecurityActions.TwoFactorDisabled, user.Id, user.TenantId, null);
        }

        var now = clock.GetUtcNow();
        var wasLocked = entry.Property(u => u.LockoutEnd).OriginalValue > now;
        var locked = user.LockoutEnd > now;
        if (locked && (!wasLocked || Changed(entry, u => u.LockoutEnd)))
        {
            // failed attempts lock while nobody (a sign-in page) or the user himself is acting; anyone else is an administrator
            var row = Event(SecurityActions.LockedOut, user.Id, user.TenantId, new { until = user.LockoutEnd });
            row.Reason = row.ActorId is null ? "FailedAttempts" : "Administrator";
            yield return row;
        }
        else if (wasLocked && !locked)
        {
            yield return Event(SecurityActions.Unlocked, user.Id, user.TenantId, null);
        }
    }

    private static bool Changed<T>(EntityEntry<User> entry, System.Linq.Expressions.Expression<Func<User, T>> property) =>
        !EqualityComparer<T>.Default.Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue);

    private SecurityEvent Event(string action, Guid user, Guid? tenant, object? details) =>
        log.Create(new SecurityLogEntry(action, user, tenant, Details: details));

    private static IQueryable<RoleName> RoleNames(DbContext context, List<Guid> ids) =>
        context.Set<Role>().IgnoreQueryFilters().Where(r => ids.Contains(r.Id)).Select(r => new RoleName(r.Id, r.Name, r.TenantId));

    private sealed record RoleName(Guid Id, string? Name, Guid? TenantId);
}
