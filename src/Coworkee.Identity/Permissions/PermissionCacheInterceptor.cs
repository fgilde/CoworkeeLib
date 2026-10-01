using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Permissions;

internal sealed class PermissionCacheInterceptor(PermissionCache cache) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> RelevantTypes =
    [
        typeof(PermissionGrant), typeof(IdentityUserRole<Guid>), typeof(UserGroupRole), typeof(UserGroupMember), typeof(Role), typeof(UserGroup), typeof(User),
    ];

    private bool _pending;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_pending)
        {
            _pending = false;
            await cache.InvalidateAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (_pending)
        {
            _pending = false;
            cache.InvalidateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending = false;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && RelevantTypes.Contains(e.Metadata.ClrType)) == true;
}
