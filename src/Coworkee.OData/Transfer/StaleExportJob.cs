using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Transfer;

/// <summary>
/// Every five minutes: exports running longer than <see cref="CoworkeeODataOptions.ExportTimeout"/> plus <see cref="Margin"/> (a server stopped while writing them),
/// or queued for <see cref="QueuedTimeout"/> without starting, fail.
/// </summary>
public sealed class StaleExportJob(CoworkeeDbContext db, IOptions<CoworkeeODataOptions> options, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-stale-exports";
    public const string Cron = "*/5 * * * *";
    public const string Message = "The export took too long.";

    // ponytail: at most this many per run; the rest follow five minutes later
    private const int Batch = 500;

    /// <summary>Running exports get this much longer than <see cref="CoworkeeODataOptions.ExportTimeout"/>, so a streamed export times out itself first.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    /// <summary>A queued export that never started in this time was lost by the job queue.</summary>
    public static readonly TimeSpan QueuedTimeout = TimeSpan.FromDays(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var jobs = await db.Set<DataTransferJob>().IgnoreQueryFilters().Where(Stale(now, options.Value.ExportTimeout))
            .OrderBy(j => j.CreatedAt).Take(Batch).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            Fail(job, now);
        }

        await SaveAsync(db, cancellationToken);
    }

    /// <summary>Exports running since longer than the timeout and the margin, or queued for a day without starting.</summary>
    internal static System.Linq.Expressions.Expression<Func<DataTransferJob, bool>> Stale(DateTimeOffset now, TimeSpan timeout)
    {
        var running = now - timeout - Margin;
        var queued = now - QueuedTimeout;
        return j => j.Kind == DataTransferKind.Export
            && ((j.Status == DataTransferStatus.Running && (j.StartedAt ?? j.CreatedAt) < running) || (j.Status == DataTransferStatus.Queued && j.CreatedAt < queued));
    }

    internal static void Fail(DataTransferJob job, DateTimeOffset now) => (job.Status, job.Message, job.FinishedAt) = (DataTransferStatus.Failed, Message, now);

    /// <summary>Saves the failed jobs; one that finished meanwhile keeps its end (the next sweep looks again).</summary>
    internal static async Task SaveAsync(CoworkeeDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            foreach (var entry in db.ChangeTracker.Entries<DataTransferJob>())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
