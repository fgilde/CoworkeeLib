using Coworkee.Contracts.Retention;

namespace Coworkee.Client.Blazor.Retention;

public interface IRetentionApi
{
    Task<IReadOnlyList<RetentionTargetDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<long> CountAsync(string key, int days, CancellationToken cancellationToken = default);

    /// <summary>Days per target key; null keeps everything.</summary>
    Task SetDaysAsync(IReadOnlyDictionary<string, int?> days, CancellationToken cancellationToken = default);

    /// <summary>Queues a run that deletes what is older than <paramref name="days"/>.</summary>
    Task RunAsync(string key, int days, CancellationToken cancellationToken = default);
}
