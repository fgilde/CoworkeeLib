using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.History;

public sealed record GetEntityHistory(string EntitySet, Guid Id) : IQuery<Result<EntityHistoryDto>>;

public sealed record PreviewEntityRestore(string EntitySet, Guid Id, EntityHistoryRestoreRequest Request) : IQuery<Result<EntityHistoryPreviewDto>>;

public sealed record RestoreEntity(string EntitySet, Guid Id, EntityHistoryRestoreRequest Request) : ICommand<Result>;

public sealed record GetDeletedRecords(string EntitySet) : IQuery<Result<IReadOnlyList<DeletedRecordDto>>>;

public sealed record GetEntityHistorySets : IQuery<Result<IReadOnlyList<EntityHistorySetDto>>>;

internal sealed class EntityHistoryHandlers(
    CoworkeeDbContext db, IEnumerable<EntityHistoryRegistration> registrations, IPermissionChecker permissions, ICurrentUser currentUser,
    IDispatcher dispatcher, IServiceProvider services)
    : IHandler<GetEntityHistory, Result<EntityHistoryDto>>,
      IHandler<PreviewEntityRestore, Result<EntityHistoryPreviewDto>>,
      IHandler<RestoreEntity, Result>,
      IHandler<GetDeletedRecords, Result<IReadOnlyList<DeletedRecordDto>>>,
      IHandler<GetEntityHistorySets, Result<IReadOnlyList<EntityHistorySetDto>>>
{
    private const int DeletedLimit = 100;

    public async Task<Result<EntityHistoryDto>> HandleAsync(GetEntityHistory query, CancellationToken cancellationToken)
    {
        var (record, error) = await LoadAsync(query.EntitySet, query.Id, restore: false, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var canRestore = await permissions.IsGrantedAsync(record!.Registration.RestorePermission, cancellationToken);
        var changes = record.Entries.SelectMany(e => e.Changes).Where(c => !record.Fields.IsHidden(c.Property)).ToList();
        await record.Texts.LoadAsync(changes.SelectMany(c => new[] { (c.Property, c.OldValue), (c.Property, c.NewValue) }), cancellationToken);
        var names = await ActorNames.LoadAsync(services, record.Entries.Select(e => e.ActorId), cancellationToken);
        var entries = record.Entries.AsEnumerable().Reverse()
            .Select(e => new EntityHistoryEntryDto(
                e.Id, e.Action.ToString(), e.ActorId, e.ActorId is { } actor ? names.GetValueOrDefault(actor) : null, e.OccurredAt,
                e.Changes.Where(c => !record.Fields.IsHidden(c.Property))
                    .Select(c => new EntityHistoryChangeDto(
                        c.Property, c.OldValue, c.NewValue, record.Texts.Text(c.Property, c.OldValue), record.Texts.Text(c.Property, c.NewValue),
                        canRestore && e.Action != AuditAction.Created && record.Fields.Restorable(c.Property) is { } p && record.Fields.CanTake(p, c.OldValue)))
                    .ToList()))
            .ToList();
        return new EntityHistoryDto(canRestore, record.IsDeleted, entries);
    }

    public async Task<Result<EntityHistoryPreviewDto>> HandleAsync(PreviewEntityRestore query, CancellationToken cancellationToken)
    {
        var (record, error) = await LoadAsync(query.EntitySet, query.Id, restore: true, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var (target, targetError) = record!.Target(query.Request);
        if (targetError is not null)
        {
            return targetError;
        }

        var differences = record.Differences(target!).ToList();
        var later = record.ChangedLater(query.Request);
        await record.Texts.LoadAsync(differences.SelectMany(p => new[] { (p.Name, record.Current[p.Name]), (p.Name, target![p.Name]) }), cancellationToken);
        return new EntityHistoryPreviewDto(record.IsDeleted, differences
            .Select(p => new EntityHistoryFieldDto(
                p.Name, record.Texts.Text(p.Name, record.Current[p.Name]), record.Texts.Text(p.Name, target![p.Name]), later.Contains(p.Name)))
            .ToList(), record.Version);
    }

    public async Task<Result> HandleAsync(RestoreEntity command, CancellationToken cancellationToken)
    {
        var (record, error) = await LoadAsync(command.EntitySet, command.Id, restore: true, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var (target, targetError) = record!.Target(command.Request);
        if (targetError is not null)
        {
            return targetError;
        }

        if (command.Request.Version != record.Version)
        {
            return HistoryErrors.Changed;
        }

        // the undelete has to reach the database before the update command looks the record up through the soft delete filter
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (record.IsDeleted && await record.Registration.UndeleteAsync(db, command.Id, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        record.Fields.Write(record.Entity, target!);
        var result = await record.Registration.SendAsync(dispatcher, record.Entity, cancellationToken);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<DeletedRecordDto>>> HandleAsync(GetDeletedRecords query, CancellationToken cancellationToken)
    {
        var (registration, error) = await AuthorizeAsync(query.EntitySet, restore: true, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var deleted = (await registration!.DeletedAsync(services, DeletedLimit, cancellationToken)).Cast<ISoftDelete>().ToList();
        var names = await ActorNames.LoadAsync(services, deleted.Select(d => d.DeletedBy), cancellationToken);
        IReadOnlyList<DeletedRecordDto> records = deleted
            .Select(d => new DeletedRecordDto(
                ((Entity)d).Id.ToString(), EntityHistoryTexts.Describe(d), d.DeletedAt ?? default, d.DeletedBy is { } by ? names.GetValueOrDefault(by) : null))
            .ToList();
        return Result<IReadOnlyList<DeletedRecordDto>>.Success(records);
    }

    public async Task<Result<IReadOnlyList<EntityHistorySetDto>>> HandleAsync(GetEntityHistorySets query, CancellationToken cancellationToken)
    {
        var sets = new List<EntityHistorySetDto>();
        if (!currentUser.IsAuthenticated || !await permissions.IsGrantedAsync(AuditPermissions.View, cancellationToken))
        {
            return Result<IReadOnlyList<EntityHistorySetDto>>.Success(sets);
        }

        foreach (var registration in registrations)
        {
            if (await ODataEntityAccess.IsGrantedAsync(services, registration.EntityType, cancellationToken))
            {
                sets.Add(new EntityHistorySetDto(
                    registration.EntityType.Name, registration.EntitySet, await permissions.IsGrantedAsync(registration.RestorePermission, cancellationToken),
                    registration.EntityType.IsAssignableTo(typeof(ISoftDelete))));
            }
        }

        return Result<IReadOnlyList<EntityHistorySetDto>>.Success(sets);
    }

    private async Task<(EntityHistoryRecord? Record, Error? Error)> LoadAsync(string entitySet, Guid id, bool restore, CancellationToken cancellationToken)
    {
        var (registration, error) = await AuthorizeAsync(entitySet, restore, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        if (await registration!.FindAsync(services, id, cancellationToken) is not { } entity)
        {
            return (null, HistoryErrors.NotFound);
        }

        var type = db.Model.FindEntityType(registration.EntityType)!;
        // properties the user may not read neither show their changes nor go back
        var hidden = (services.GetService<ODataEntityRegistry>()?.Find(registration.EntityType)?.HiddenProperties ?? [])
            .Concat(await ODataEntityAccess.DeniedPropertiesAsync(services, registration.EntityType, cancellationToken)).ToList();
        var entries = await (await AuditAsync(cancellationToken))
            .Where(e => e.EntityType == registration.EntityType.Name && e.EntityId == id.ToString())
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        var fields = new EntityHistoryFields(type, hidden);
        return (new EntityHistoryRecord(registration, entity, fields, new EntityHistoryTexts(db, services, type), entries), null);
    }

    private async Task<(EntityHistoryRegistration? Registration, Error? Error)> AuthorizeAsync(string entitySet, bool restore, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return (null, Error.Unauthorized("auth.required", "Authentication is required."));
        }

        if (registrations.FirstOrDefault(r => string.Equals(r.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase)) is not { } registration)
        {
            return (null, HistoryErrors.UnknownSet);
        }

        var granted = await permissions.IsGrantedAsync(AuditPermissions.View, cancellationToken)
            && await ODataEntityAccess.IsGrantedAsync(services, registration.EntityType, cancellationToken)
            && (!restore || await permissions.IsGrantedAsync(registration.RestorePermission, cancellationToken));
        return granted ? (registration, null) : (null, Error.Forbidden("auth.forbidden", "You may not see or restore this history."));
    }

    private async Task<IQueryable<AuditEntry>> AuditAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var system = tenantId is { } tenant && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenant, cancellationToken);
        return db.Set<AuditEntry>().AsNoTracking().Where(e => e.TenantId == tenantId || (system && e.TenantId == null));
    }
}

internal static class HistoryErrors
{
    public static readonly Error NotFound = Error.NotFound("history.not_found", "The record or the history entry does not exist.");
    public static readonly Error UnknownSet = Error.NotFound("history.unknown_set", "The entity set has no history.");
    public static readonly Error NotRestorable = Error.Validation("property", "This change cannot be undone.");
    public static readonly Error Changed = Error.Conflict("history.changed", "The record changed since the preview. Check the preview again before you restore.");
}
