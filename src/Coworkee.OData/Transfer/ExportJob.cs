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
        DataExportMail? mail = null;
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
            mail = args.Mail ? new DataExportMail(job.UserId, args.Recipients ?? [], job.EntitySet, file with { Name = job.ResultFileName }) : null;
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

        // Running -> Completed or Failed only while it still runs; the mail goes only after that applied, so a cancel or the sweep that came first wins
        job.FinishedAt = clock.GetUtcNow();
        if (!await SavedAsync(job.Id, CancellationToken.None) || mail is null)
        {
            return;
        }

        try
        {
            await exports.MailAsync(mail, cancellationToken);
        }
        catch (Exception exception)
        {
            // the file is there; the job stays completed and says that the mail failed
            logger.LogError(exception, "The mail of export {JobId} failed.", job.Id);
            job.Message = MailFailed;
            await SavedAsync(job.Id, CancellationToken.None);
        }
    }

    public const string MailFailed = "The file is ready, but the mail could not be sent.";

    /// <summary>False when the job changed meanwhile (the stale sweep failed it, the user cancelled it): its state stays, nothing of this run is written, also no mail.</summary>
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
