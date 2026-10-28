using System.Collections.Concurrent;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

/// <summary>
/// Stops running exports the user cancelled. The job row is the source of truth (<see cref="DataTransferStatus.Cancelled"/>): a watch trips at once on the
/// instance that took the cancel request and within <see cref="Poll"/> on the others, which read the row.
/// </summary>
public sealed class ExportCancellations(IServiceScopeFactory scopes)
{
    private readonly ConcurrentDictionary<Guid, Watch> _watches = new();

    /// <summary>How often a watch reads its job row.</summary>
    internal TimeSpan Poll { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>A token for the export of <paramref name="jobId"/> that trips with <paramref name="token"/> or when the job is cancelled.</summary>
    internal Watch Start(Guid jobId, CancellationToken token)
    {
        var watch = new Watch(this, jobId, token);
        _watches[jobId] = watch;
        _ = PollAsync(watch, watch.Token);
        return watch;
    }

    /// <summary>Trips the watch of a job running on this instance.</summary>
    internal void Trip(Guid jobId)
    {
        if (_watches.TryGetValue(jobId, out var watch))
        {
            watch.Cancel();
        }
    }

    // ponytail: one row read per running export and poll; a shared poll over all running jobs if instances run many exports at once
    private async Task PollAsync(Watch watch, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Poll, token);
                await using var scope = scopes.CreateAsyncScope();
                var status = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<DataTransferJob>().IgnoreQueryFilters()
                    .Where(j => j.Id == watch.JobId).Select(j => (DataTransferStatus?)j.Status).FirstOrDefaultAsync(token);
                if (status == DataTransferStatus.Cancelled)
                {
                    watch.Cancel();
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    internal sealed class Watch : IDisposable
    {
        private readonly ExportCancellations _owner;
        private readonly CancellationTokenSource _source;

        public Watch(ExportCancellations owner, Guid jobId, CancellationToken token)
        {
            (_owner, JobId) = (owner, jobId);
            _source = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public Guid JobId { get; }

        public CancellationToken Token => _source.Token;

        /// <summary>Whether the user cancelled the job (not a timeout or shutdown).</summary>
        public bool Cancelled { get; private set; }

        public void Cancel()
        {
            Cancelled = true;
            try
            {
                _source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _owner._watches.TryRemove(new KeyValuePair<Guid, Watch>(JobId, this));
            try
            {
                _source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _source.Dispose();
        }
    }
}
