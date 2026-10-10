using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Permissions;

/// <summary>Drops the cached permissions after a relevant change, and again once its transaction commits, so no request caches the old state meanwhile.</summary>
internal sealed class PermissionCacheInterceptor(PermissionCache cache) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly HashSet<Type> RelevantTypes =
    [
        typeof(PermissionGrant), typeof(IdentityUserRole<Guid>), typeof(UserGroupRole), typeof(UserGroupMember), typeof(Role), typeof(UserGroup), typeof(User),
        typeof(IdentityUserClaim<Guid>),
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
        await InvalidateAsync(eventData.Context?.Database.CurrentTransaction is null, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidateAsync(eventData.Context?.Database.CurrentTransaction is null, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        InvalidateAsync(true, cancellationToken);

    public void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) =>
        InvalidateAsync(true, CancellationToken.None).GetAwaiter().GetResult();

    public void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => _pending = false;

    public Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending = false;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private async Task InvalidateAsync(bool committed, CancellationToken cancellationToken)
    {
        if (!_pending)
        {
            return;
        }

        _pending = !committed;
        await cache.InvalidateAsync(cancellationToken);
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && RelevantTypes.Contains(e.Metadata.ClrType)) == true;
}
