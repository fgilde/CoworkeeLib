using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// Drops the cached filters of every organisation (on every replica through the cache's tags) once a data filter or link is saved or deleted, and again
/// once its transaction commits: a read on another connection meanwhile still sees the old rows and may cache them.
/// </summary>
internal sealed class DataFilterCacheInterceptor(HybridCache cache) : SaveChangesInterceptor, IDbTransactionInterceptor
{
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

    // dropped at save as well: this scope's next read (inside the transaction, uncached) must not meet an entry another request filled meanwhile
    private async Task InvalidateAsync(bool committed, CancellationToken cancellationToken)
    {
        if (!_pending)
        {
            return;
        }

        _pending = !committed;
        await cache.RemoveByTagAsync(DataFilterCache.Tag, cancellationToken);
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries().Any(e => e.Entity is DataFilter or DataFilterLink && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) == true;
}
