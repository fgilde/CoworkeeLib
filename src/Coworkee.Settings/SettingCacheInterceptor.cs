using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Settings;

internal sealed class SettingCacheInterceptor(HybridCache cache) : SaveChangesInterceptor
{
    private bool _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_pending)
        {
            _pending = false;
            await cache.RemoveByTagAsync(SettingProvider.CacheTag, cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (_pending)
        {
            _pending = false;
            cache.RemoveByTagAsync(SettingProvider.CacheTag).AsTask().GetAwaiter().GetResult();
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
        _pending |= context?.ChangeTracker.Entries<SettingValue>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) == true;
}
