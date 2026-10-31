using Coworkee.Application.Groups;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            var progress = new SavedProgress(services.GetRequiredService<IServiceScopeFactory>(), job.Id);
            ExportFile file;
            using (var saving = CancellationTokenSource.CreateLinkedTokenSource(watch.Token))
            {
                var saver = progress.SaveAsync(saving.Token);
                try
                {
                    file = (await exports.WriteAsync(entity, new QueryString(job.Settings), null, watch.Token, progress))!;
                }
                finally
                {
                    await saving.CancelAsync();
                    await saver;
                }
            }

            (job.Result, job.ResultContentType, job.ResultFileName) = (file.Content, file.ContentType, job.FileName ?? file.Name);
            (job.Total, job.Processed) = (progress.Last?.Total ?? 0, progress.Last?.Rows ?? 0);
            job.Status = DataTransferStatus.Completed;
            mail = new DataExportMail(job.UserId, args.Recipients ?? [], job.EntitySet, file with { Name = job.ResultFileName });
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

        // read after the completed status is saved: a stream whose client left sets the flag only while the job runs, else it mails the file itself
        if (!args.Mail && !await db.Set<DataTransferJob>().AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.MailOwner).SingleAsync(CancellationToken.None))
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

    /// <summary>How often a running export saves its progress (rows read of all) on the job, for the stream that follows it.</summary>
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(1);

    /// <summary>Keeps the last progress of the export and saves it on the job row from a scope of its own: the job's context is busy reading the rows.</summary>
    private sealed class SavedProgress(IServiceScopeFactory scopes, Guid jobId) : IProgress<DataExportProgress>
    {
        private volatile DataExportProgress? _last;

        public DataExportProgress? Last => _last;

        public void Report(DataExportProgress value) => _last = value;

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            DataExportProgress? saved = null;
            try
            {
                while (true)
                {
                    await Task.Delay(ProgressEvery, cancellationToken);
                    if (_last is { Total: { } total } last && last != saved)
                    {
                        saved = last;
                        await using var scope = scopes.CreateAsyncScope();
                        // no change tracker: only a running job takes it (a cancel or the sweep wins), and no realtime event per step
                        await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<DataTransferJob>().IgnoreQueryFilters()
                            .Where(j => j.Id == jobId && j.Status == DataTransferStatus.Running)
                            .ExecuteUpdateAsync(j => j.SetProperty(x => x.Total, total).SetProperty(x => x.Processed, last.Rows ?? 0), cancellationToken);
                    }
                }
            }
            catch (Exception)
            {
                // progress only: the export goes on without it
            }
        }
    }

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
