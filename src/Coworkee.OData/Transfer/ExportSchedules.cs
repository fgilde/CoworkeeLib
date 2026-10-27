using System.ComponentModel;
using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Transfer;

/// <summary>A list export that is mailed on a schedule to its owner and further recipients; each run is a job of the owner with the file.</summary>
public sealed class ExportSchedule : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string EntitySet { get; set; }

    public required string Name { get; set; }

    /// <summary>The query string of the export ($filter, $search, $orderby, columns, format), kept verbatim.</summary>
    public string? Query { get; set; }

    public string? FileName { get; set; }

    public List<string> Recipients { get; set; } = [];

    /// <summary>The groups selected when the schedule was saved; every run lists their rows, as the owner's list did.</summary>
    public List<Guid> Groups { get; set; } = [];

    public SubscriptionFrequency Frequency { get; set; }

    public TimeOnly Time { get; set; }

    public required string TimeZone { get; set; }

    public DayOfWeek? Weekday { get; set; }

    public int? DayOfMonth { get; set; }

    public int Every { get; set; } = 1;

    public MonthWeek? Week { get; set; }

    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ReportSchedule Schedule => new(Frequency, Time, TimeZone, Weekday, DayOfMonth, Every, Week);

    public void Apply(ReportSchedule schedule, DateTimeOffset now)
    {
        var byWeek = schedule is { Frequency: SubscriptionFrequency.Monthly, Week: not null };
        (Frequency, Time, TimeZone, Every) = (schedule.Frequency, schedule.Time, schedule.TimeZone, Math.Max(1, schedule.Every));
        Weekday = schedule.Frequency == SubscriptionFrequency.Weekly || byWeek ? schedule.Weekday : null;
        DayOfMonth = schedule.Frequency == SubscriptionFrequency.Monthly && !byWeek ? schedule.DayOfMonth : null;
        Week = byWeek ? schedule.Week : null;
        NextRunAt = ReportSchedules.Next(Schedule, now).ToUniversalTime();
    }

    internal ExportScheduleDto ToDto() => new(Id, EntitySet, Name, Query, Schedule, FileName, Recipients, NextRunAt, LastRunAt, LastError);
}

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Lists the user's scheduled list exports, optionally of one entity set.")]
public sealed record GetExportSchedulesQuery(string? EntitySet = null) : IQuery<Result<IReadOnlyList<ExportScheduleDto>>>;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Creates (no id) or changes a scheduled list export that is mailed to the user and further recipients.")]
public sealed record SaveExportScheduleCommand(Guid? Id, SaveExportScheduleRequest Schedule) : ICommand<Result<ExportScheduleDto>>;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Deletes one of the user's scheduled list exports.")]
public sealed record DeleteExportScheduleCommand(Guid Id) : ICommand<Result>;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Runs one of the user's scheduled list exports now; the schedule stays.")]
public sealed record RunExportScheduleCommand(Guid Id) : ICommand<Result<ExportScheduleDto>>;

internal sealed class SaveExportScheduleValidator : AbstractValidator<SaveExportScheduleCommand>
{
    public SaveExportScheduleValidator()
    {
        RuleFor(c => c.Schedule).NotNull();
        RuleFor(c => c.Schedule.EntitySet).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Schedule.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Schedule.Query).MaximumLength(4000);
        RuleFor(c => c.Schedule.FileName).MaximumLength(200);
        RuleFor(c => c.Schedule.Schedule).NotNull().SetValidator(new Application.Calendar.ScheduleValidator());
        RuleFor(c => c.Schedule.Recipients).Must(r => DataExports.InvalidRecipients(Clean(r)) is null)
            .WithMessage(c => DataExports.InvalidRecipients(Clean(c.Schedule.Recipients)));
    }

    internal static List<string> Clean(IReadOnlyList<string>? recipients) =>
        recipients?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
}

internal sealed class ExportScheduleHandlers(
    CoworkeeDbContext db, IServiceProvider services, ODataEntityRegistry registry, ExportScheduleRunner runner, ICurrentUser user, TimeProvider clock)
    : IHandler<GetExportSchedulesQuery, Result<IReadOnlyList<ExportScheduleDto>>>,
      IHandler<SaveExportScheduleCommand, Result<ExportScheduleDto>>,
      IHandler<DeleteExportScheduleCommand, Result>,
      IHandler<RunExportScheduleCommand, Result<ExportScheduleDto>>
{
    private static readonly Error NotFound = Error.NotFound("export_schedules.not_found", "There is no such scheduled export.");

    public async Task<Result<IReadOnlyList<ExportScheduleDto>>> HandleAsync(GetExportSchedulesQuery query, CancellationToken cancellationToken)
    {
        var schedules = await Mine().AsNoTracking().Where(s => query.EntitySet == null || s.EntitySet == query.EntitySet).OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return schedules.Select(s => s.ToDto()).ToList();
    }

    public async Task<Result<ExportScheduleDto>> HandleAsync(SaveExportScheduleCommand command, CancellationToken cancellationToken)
    {
        var request = command.Schedule;
        if (registry.Find(request.EntitySet) is not { } entity)
        {
            return Error.NotFound("data.set_not_found", "There is no such entity set.");
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not read this entity set.");
        }

        var schedule = command.Id is { } id ? await Mine().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) : null;
        if (command.Id is not null && schedule is null)
        {
            return NotFound;
        }

        if (schedule is null)
        {
            schedule = new ExportSchedule
            {
                TenantId = user.TenantId ?? Guid.Empty,
                UserId = user.UserId!.Value,
                EntitySet = entity.EntitySet,
                Name = request.Name,
                TimeZone = request.Schedule.TimeZone,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Add(schedule);
        }

        schedule.EntitySet = entity.EntitySet;
        schedule.Name = request.Name.Trim();
        schedule.Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        schedule.FileName = string.IsNullOrWhiteSpace(request.FileName) ? null : request.FileName.Trim();
        schedule.Recipients = SaveExportScheduleValidator.Clean(request.Recipients);
        schedule.Groups = services.GetService<IGroupScope>() is { } scope ? [.. await scope.SelectedAsync(cancellationToken)] : [];
        schedule.Apply(request.Schedule, clock.GetUtcNow());
        return schedule.ToDto();
    }

    public async Task<Result> HandleAsync(DeleteExportScheduleCommand command, CancellationToken cancellationToken)
    {
        if (await Mine().SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken) is not { } schedule)
        {
            return NotFound;
        }

        db.Remove(schedule);
        return Result.Success();
    }

    public async Task<Result<ExportScheduleDto>> HandleAsync(RunExportScheduleCommand command, CancellationToken cancellationToken)
    {
        if (await Mine().SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken) is not { } schedule)
        {
            return NotFound;
        }

        await runner.RunAsync(schedule, clock.GetUtcNow(), advance: false, cancellationToken);
        return schedule.ToDto();
    }

    private IQueryable<ExportSchedule> Mine() => db.Set<ExportSchedule>().Where(s => s.UserId == user.UserId);
}

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

            error = exception is UnauthorizedAccessException ? exception.Message : "The export failed.";
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

/// <summary>Runs the due export schedules, organisation by organisation, each as its owner.</summary>
public sealed class ExportScheduleJob(CoworkeeDbContext db, TenantJobRunner tenants, IDistributedLock locks, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-export-schedules";
    public const string Cron = "*/5 * * * *";

    // ponytail: at most this many exports per run; the rest follow five minutes later
    private const int Batch = 100;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<ExportSchedule>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Take(Batch)
            .Select(s => new { s.Id, s.TenantId, s.UserId })
            .ToListAsync(cancellationToken);
        var byTenant = due.ToLookup(s => s.TenantId);
        await tenants.ForTenantsAsync(byTenant.Select(g => g.Key), async (services, tenantId, ct) =>
        {
            foreach (var schedule in byTenant[tenantId])
            {
                // the owner's permissions, row filters and groups decide what the file holds
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(schedule.UserId, tenantId));
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
                if (await scopedDb.Set<ExportSchedule>().SingleOrDefaultAsync(s => s.Id == schedule.Id, ct) is { } row)
                {
                    await scope.ServiceProvider.GetRequiredService<ExportScheduleRunner>().RunAsync(row, now, advance: true, ct);
                    await scopedDb.SaveChangesAsync(ct);
                }
            }
        }, cancellationToken);
    }
}
