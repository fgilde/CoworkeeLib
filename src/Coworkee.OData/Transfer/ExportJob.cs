using Coworkee.Application.Groups;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Transfer;

internal sealed class ExportJob(IServiceProvider services, DataExports exports, CoworkeeDbContext db, ODataEntityRegistry registry, TimeProvider clock, ILogger<ExportJob> logger)
    : IBackgroundJob<ExportJobArgs>
{
    public async Task ExecuteAsync(ExportJobArgs args, CancellationToken cancellationToken)
    {
        if (await db.Set<DataTransferJob>().SingleOrDefaultAsync(j => j.Id == args.JobId, cancellationToken) is not { Status: DataTransferStatus.Queued } job)
        {
            return;
        }

        job.Status = DataTransferStatus.Running;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var entity = registry.Find(job.EntitySet) ?? throw new InvalidOperationException($"{job.EntitySet} is no OData set anymore.");
            if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
            {
                throw new UnauthorizedAccessException($"You may not read {job.EntitySet}.");
            }

            using var selection = args.Groups is { Length: > 0 } groups ? GroupScope.Selection(groups) : null;
            var file = (await exports.WriteAsync(entity, new QueryString(job.Settings), null, cancellationToken))!;
            (job.Result, job.ResultContentType, job.ResultFileName) = (file.Content, file.ContentType, job.FileName ?? file.Name);
            job.Status = DataTransferStatus.Completed;
            if (args.Mail)
            {
                await exports.MailAsync(new DataExportMail(job.UserId, args.Recipients ?? [], job.EntitySet, file with { Name = job.ResultFileName }), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Export {JobId} failed.", job.Id);
            (job.Status, job.Message) = (DataTransferStatus.Failed, exception.Message[..Math.Min(exception.Message.Length, 2000)]);
        }

        job.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
