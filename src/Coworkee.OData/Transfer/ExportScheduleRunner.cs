using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Transfer;

/// <summary>
/// One run of a schedule as its owner: the export becomes a job with the file, the file goes by mail; errors stay on the schedule.
/// Nothing is sent once the owner is deactivated or lost DataTransfer.ExportSchedules or the set's permission.
/// </summary>
internal sealed partial class ExportScheduleRunner(
    IServiceProvider services, CoworkeeDbContext db, DataExports exports, ODataEntityRegistry registry, TimeProvider clock, ILogger<ExportScheduleRunner> logger)
{
    public async Task RunAsync(ExportSchedule schedule, DateTimeOffset now, bool advance, CancellationToken cancellationToken)
    {
        // a failed run takes back what it added, nothing that was waiting in the unit of work before (Send now runs inside a request)
        var before = db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        string? error = null;
        try
        {
            if (services.GetService<IUserDirectory>() is { } users && !(await users.GetActiveEmailsAsync([schedule.UserId], cancellationToken)).ContainsKey(schedule.UserId))
            {
                error = "The owner is no longer active.";
            }
            else if (!await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(DataTransferPermissions.ExportSchedules, cancellationToken))
            {
                error = "The owner may no longer schedule exports.";
            }
            else if (registry.Find(schedule.EntitySet) is not { } entity)
            {
                error = $"{schedule.EntitySet} is no entity set anymore.";
            }
            else if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
            {
                error = $"The owner may no longer read {schedule.EntitySet}.";
            }
            else if (await DataExports.GroupsAsync(services, schedule.Groups, cancellationToken) is { Count: 0 })
            {
                error = DataExports.NoGroupsLeft;
            }
            else
            {
                var query = DataExports.Query(schedule.Query);
                using var selection = await DataExports.GroupsAsync(services, schedule.Groups, cancellationToken) is { } groups ? GroupScope.Selection(groups) : null;
                var file = (await exports.WriteAsync(entity, query, null, cancellationToken))!;
                var job = exports.NewJob(entity, query, schedule.FileName ?? schedule.Name, DataTransferStatus.Completed);
                (job.Result, job.ResultContentType, job.ResultFileName, job.FinishedAt) = (file.Content, file.ContentType, job.FileName, clock.GetUtcNow());
                db.Add(job);
                await exports.MailAsync(new DataExportMail(schedule.UserId, schedule.Recipients, schedule.Name, file with { Name = job.FileName! }), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, schedule.Id);
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added && !before.Contains(e.Entity)).ToList())
            {
                entry.State = EntityState.Detached;
            }

            error = exception switch
            {
                UnauthorizedAccessException => exception.Message,
                Microsoft.OData.ODataException => $"The query is not valid: {exception.Message}",
                _ => "The export failed.",
            };
        }

        schedule.LastRunAt = now;
        schedule.LastError = error;
        if (advance)
        {
            schedule.NextRunAt = ReportSchedules.Next(schedule.Schedule, now).ToUniversalTime();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The scheduled export {ScheduleId} failed")]
    private partial void LogFailed(Exception exception, Guid scheduleId);
}
