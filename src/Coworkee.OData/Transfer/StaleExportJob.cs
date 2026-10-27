using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Transfer;

/// <summary>Every five minutes: exports running or queued longer than <see cref="CoworkeeODataOptions.ExportTimeout"/> (a server stopped while writing them) fail.</summary>
public sealed class StaleExportJob(CoworkeeDbContext db, IOptions<CoworkeeODataOptions> options, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-stale-exports";
    public const string Cron = "*/5 * * * *";
    public const string Message = "The export took too long.";

    // ponytail: at most this many per run; the rest follow five minutes later
    private const int Batch = 500;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stale = now - options.Value.ExportTimeout;
        var jobs = await db.Set<DataTransferJob>().IgnoreQueryFilters()
            .Where(j => j.Kind == DataTransferKind.Export && (j.Status == DataTransferStatus.Running || j.Status == DataTransferStatus.Queued) && j.CreatedAt < stale)
            .OrderBy(j => j.CreatedAt).Take(Batch).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            Fail(job, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static void Fail(DataTransferJob job, DateTimeOffset now) => (job.Status, job.Message, job.FinishedAt) = (DataTransferStatus.Failed, Message, now);
}
