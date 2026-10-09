namespace Coworkee.Application;

/// <summary>A lock held across all instances of the app; dispose the handle to release it.</summary>
public interface IDistributedLock
{
    /// <summary>Waits up to <paramref name="timeout"/> for the lock; null when another holder kept it that long.</summary>
    Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default);
}
