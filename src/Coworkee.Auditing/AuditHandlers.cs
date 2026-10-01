using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing;

[RequiresPermission(AuditPermissions.View)]
public sealed record GetAuditEntries(AuditQuery Query) : IQuery<Result<PagedResult<AuditEntryDto>>>;

public sealed record GetEntityVersions(string Type, Guid Id) : IQuery<Result<IReadOnlyList<EntityVersionDto>>>;

public sealed record GetEntityVersion(string Type, Guid Id, int Revision) : IQuery<Result<EntityVersionDetailDto>>;

public sealed record RestoreEntityVersion(string Type, Guid Id, int Revision) : ICommand<Result>;

internal sealed class AuditQueryHandler(CoworkeeDbContext db, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetAuditEntries, Result<PagedResult<AuditEntryDto>>>
{
    public async Task<Result<PagedResult<AuditEntryDto>>> HandleAsync(GetAuditEntries request, CancellationToken cancellationToken)
    {
        var query = request.Query;
        var tenantId = currentUser.TenantId;
        var system = tenantId is { } tenant && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenant, cancellationToken);
        var entries = db.Set<AuditEntry>().AsNoTracking().Where(e => e.TenantId == tenantId || (system && e.TenantId == null));
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            entries = entries.Where(e => e.EntityId == query.EntityId);
        }

        if (query.ActorId is { } actor)
        {
            entries = entries.Where(e => e.ActorId == actor);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.OccurredAt <= to);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var total = await entries.CountAsync(cancellationToken);
        var items = await entries.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var names = await ActorNames.LoadAsync(services, items.Select(e => e.ActorId), cancellationToken);
        return new PagedResult<AuditEntryDto>(
            items.Select(e => new AuditEntryDto(
                e.Id, e.EntityType, e.EntityId, e.Action.ToString(), e.ActorId, e.ActorId is { } id ? names.GetValueOrDefault(id) : null,
                e.OccurredAt, e.CorrelationId, e.Changes.Select(c => new AuditChangeDto(c.Property, c.OldValue, c.NewValue)).ToList())).ToList(),
            total, page, pageSize);
    }
}

internal sealed class VersionHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IVersionedTypeRegistry types, IPermissionChecker permissions, IServiceProvider services, TimeProvider clock)
    : IHandler<GetEntityVersions, Result<IReadOnlyList<EntityVersionDto>>>,
      IHandler<GetEntityVersion, Result<EntityVersionDetailDto>>,
      IHandler<RestoreEntityVersion, Result>
{
    private static readonly HashSet<string> Untouchable = new(StringComparer.Ordinal)
    {
        nameof(IVersioned.Revision), nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy),
        nameof(IMultiTenant.TenantId),
    };

    public async Task<Result<IReadOnlyList<EntityVersionDto>>> HandleAsync(GetEntityVersions query, CancellationToken cancellationToken)
    {
        var (type, error) = await AuthorizeAsync(query.Type, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var snapshots = await (await SnapshotsAsync(type!, query.Id, cancellationToken))
            .OrderByDescending(s => s.Revision)
            .Select(s => new { s.Revision, s.CreatedAt, s.CreatedBy, s.IsDeleted })
            .ToListAsync(cancellationToken);
        var names = await ActorNames.LoadAsync(services, snapshots.Select(s => s.CreatedBy), cancellationToken);
        IReadOnlyList<EntityVersionDto> versions = snapshots
            .Select(s => new EntityVersionDto(s.Revision, s.CreatedAt, s.CreatedBy, s.CreatedBy is { } id ? names.GetValueOrDefault(id) : null, s.IsDeleted))
            .ToList();
        return Result<IReadOnlyList<EntityVersionDto>>.Success(versions);
    }

    public async Task<Result<EntityVersionDetailDto>> HandleAsync(GetEntityVersion query, CancellationToken cancellationToken)
    {
        var (type, error) = await AuthorizeAsync(query.Type, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var snapshot = await (await SnapshotsAsync(type!, query.Id, cancellationToken)).SingleOrDefaultAsync(s => s.Revision == query.Revision, cancellationToken);
        return snapshot is null
            ? VersionErrors.NotFound
            : new EntityVersionDetailDto(snapshot.Revision, JsonDocument.Parse(snapshot.Payload).RootElement.Clone(), snapshot.IsDeleted);
    }

    public async Task<Result> HandleAsync(RestoreEntityVersion command, CancellationToken cancellationToken)
    {
        var (type, error) = await AuthorizeAsync(command.Type, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var snapshot = await (await SnapshotsAsync(type!, command.Id, cancellationToken)).SingleOrDefaultAsync(s => s.Revision == command.Revision, cancellationToken);
        var entity = snapshot is null || snapshot.IsDeleted ? null : await db.FindAsync(type!.ClrType, [command.Id], cancellationToken);
        if (entity is null)
        {
            return VersionErrors.NotFound;
        }

        Apply(db.Entry(entity), JsonDocument.Parse(snapshot!.Payload).RootElement);
        db.Add(new AuditEntry
        {
            TenantId = snapshot.TenantId,
            EntityType = snapshot.EntityType,
            EntityId = snapshot.EntityId,
            Action = AuditAction.Restored,
            ActorId = currentUser.UserId,
            OccurredAt = clock.GetUtcNow(),
            Changes = [new AuditChange { Property = "RestoredRevision", NewValue = command.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
        });
        return Result.Success();
    }

    private static void Apply(EntityEntry entry, JsonElement payload)
    {
        foreach (var property in entry.Properties.Where(p => !p.Metadata.IsKey() && !p.Metadata.IsShadowProperty() && !p.Metadata.IsConcurrencyToken && !Untouchable.Contains(p.Metadata.Name)))
        {
            if (payload.TryGetProperty(property.Metadata.Name, out var value))
            {
                property.CurrentValue = value.Deserialize(property.Metadata.ClrType);
            }
        }
    }

    private async Task<(VersionedType? Type, Error? Error)> AuthorizeAsync(string name, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return (null, Error.Unauthorized("auth.required", "Authentication is required."));
        }

        if (types.Find(name) is not { } type)
        {
            return (null, VersionErrors.UnknownType);
        }

        return await permissions.IsGrantedAsync(type.Permission, cancellationToken)
            ? (type, null)
            : (null, Error.Forbidden("auth.forbidden", $"Permission '{type.Permission}' is required."));
    }

    private async Task<IQueryable<EntitySnapshot>> SnapshotsAsync(VersionedType type, Guid id, CancellationToken cancellationToken)
    {
        var entityId = id.ToString();
        var entityType = type.ClrType.Name;
        var tenantId = currentUser.TenantId;
        var system = tenantId is { } tenant && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenant, cancellationToken);
        return db.Set<EntitySnapshot>().AsNoTracking()
            .Where(s => s.EntityType == entityType && s.EntityId == entityId && (s.TenantId == tenantId || (system && s.TenantId == null)));
    }
}

internal static class VersionErrors
{
    public static readonly Error NotFound = Error.NotFound("versions.not_found", "The version does not exist.");
    public static readonly Error UnknownType = Error.NotFound("versions.unknown_type", "The entity type is not versioned.");
}

internal static class ActorNames
{
    public static async Task<IReadOnlyDictionary<Guid, string>> LoadAsync(IServiceProvider services, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.OfType<Guid>().Distinct().ToList();
        return distinct.Count == 0 || services.GetService<IUserDirectory>() is not { } users
            ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(distinct, cancellationToken);
    }
}
