using Coworkee.Application.Groups;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Transfer;

internal sealed class ExportJob(
    IServiceProvider services, DataExports exports, CoworkeeDbContext db, ODataEntityRegistry registry, ExportCancellations cancellations, TimeProvider clock, ILogger<ExportJob> logger)
    : IBackgroundJob<ExportJobArgs>
{
    public async Task ExecuteAsync(ExportJobArgs args, CancellationToken cancellationToken)
    {
        if (await db.Set<DataTransferJob>().SingleOrDefaultAsync(j => j.Id == args.JobId, cancellationToken) is not { Status: DataTransferStatus.Queued } job)
        {
            return;
        }

        (job.Status, job.StartedAt) = (DataTransferStatus.Running, clock.GetUtcNow());
        if (!await SavedAsync(job.Id, cancellationToken))
        {
            return;
        }

        using var watch = cancellations.Start(job.Id, cancellationToken);
        try
        {
            var entity = registry.Find(job.EntitySet) ?? throw new InvalidOperationException($"{job.EntitySet} is no OData set anymore.");
            if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
            {
                throw new UnauthorizedAccessException($"You may not read {job.EntitySet}.");
            }

            var groups = await DataExports.GroupsAsync(services, args.Groups ?? [], cancellationToken);
            if (groups is { Count: 0 })
            {
                throw new UnauthorizedAccessException(DataExports.NoGroupsLeft);
            }

            using var selection = groups is null ? null : GroupScope.Selection(groups);
            var file = (await exports.WriteAsync(entity, new QueryString(job.Settings), null, watch.Token))!;
            (job.Result, job.ResultContentType, job.ResultFileName) = (file.Content, file.ContentType, job.FileName ?? file.Name);
            job.Status = DataTransferStatus.Completed;
            if (args.Mail && !watch.Cancelled)
            {
                await exports.MailAsync(new DataExportMail(job.UserId, args.Recipients ?? [], job.EntitySet, file with { Name = job.ResultFileName }), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (watch.Cancelled)
        {
            // the cancel saved the job already
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Export {JobId} failed.", job.Id);
            (job.Status, job.Message) = (DataTransferStatus.Failed, exception.Message[..Math.Min(exception.Message.Length, 2000)]);
        }

        job.FinishedAt = clock.GetUtcNow();
        await SavedAsync(job.Id, CancellationToken.None);
    }

    /// <summary>False when the job changed meanwhile (the stale sweep failed it): its state stays, nothing of this run is written, also no mail.</summary>
    private async Task<bool> SavedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Export {JobId} changed meanwhile; this run is dropped.", jobId);
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
