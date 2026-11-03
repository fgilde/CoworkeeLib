using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

internal sealed class ExportScheduleHandlers(
    CoworkeeDbContext db, IServiceProvider services, ODataEntityRegistry registry, ExportScheduleRunner runner, DataExports exports, ICurrentUser user, TimeProvider clock)
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

        // the query passes the guard of the OData sets now (readable properties, navigations, no aliases), not only at the first run
        try
        {
            await exports.WriteAsync(entity, DataExports.Query(request.Query), 0, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error.Forbidden("auth.forbidden", exception.Message);
        }
        catch (Microsoft.OData.ODataException exception)
        {
            return Error.Validation("Query", $"The query is not valid: {exception.Message}");
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
