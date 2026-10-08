using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.ExtendedAttributes;

internal sealed class ExtendedAttributeHandlers(
    CoworkeeDbContext db, IEnumerable<ExtendedAttributeRegistration> registrations, IPermissionChecker permissions, ICurrentUser currentUser)
    : IHandler<GetExtendedAttributesQuery, Result<IReadOnlyList<ExtendedAttributeDto>>>,
      IHandler<SetExtendedAttributesCommand, Result<IReadOnlyList<ExtendedAttributeDto>>>
{
    private static readonly Error NotFound = Error.NotFound("attributes.not_found", "There is no such entity.");

    private static readonly Error Forbidden = Error.Forbidden("attributes.forbidden", "You may not see or change these attributes.");

    public async Task<Result<IReadOnlyList<ExtendedAttributeDto>>> HandleAsync(GetExtendedAttributesQuery query, CancellationToken cancellationToken) =>
        await CheckAsync(query.EntityType, query.EntityId, edit: false, cancellationToken) is { } error
            ? error
            : await LoadAsync(query.EntityType, query.EntityId, cancellationToken);

    public async Task<Result<IReadOnlyList<ExtendedAttributeDto>>> HandleAsync(SetExtendedAttributesCommand command, CancellationToken cancellationToken)
    {
        if (await CheckAsync(command.EntityType, command.EntityId, edit: true, cancellationToken) is { } error)
        {
            return error;
        }

        var existing = await Attributes(command.EntityType, command.EntityId).ToDictionaryAsync(a => a.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var wanted = command.Attributes.ToDictionary(a => a.Key.Trim(), StringComparer.OrdinalIgnoreCase);
        db.RemoveRange(existing.Values.Where(a => !wanted.ContainsKey(a.Key)));
        foreach (var (key, dto) in wanted)
        {
            if (!existing.TryGetValue(key, out var attribute))
            {
                attribute = new ExtendedAttribute { TenantId = currentUser.TenantId!.Value, EntityType = command.EntityType, EntityId = command.EntityId, Key = key };
                db.Add(attribute);
            }

            Apply(attribute, dto);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(command.EntityType, command.EntityId, cancellationToken);
    }

    private async Task<Error?> CheckAsync(string entityType, Guid entityId, bool edit, CancellationToken cancellationToken)
    {
        if (registrations.FirstOrDefault(r => r.EntityType == entityType) is not { } registration)
        {
            return NotFound;
        }

        if (!await permissions.IsGrantedAsync(edit ? registration.EditPermission : registration.ViewPermission, cancellationToken))
        {
            return Forbidden;
        }

        // ponytail: only the tenant filter applies here, not row level OData filters; check them in a decorator if an entity needs that
        return await registration.ExistsAsync(db, entityId, cancellationToken) ? null : NotFound;
    }

    private IQueryable<ExtendedAttribute> Attributes(string entityType, Guid entityId) =>
        db.Set<ExtendedAttribute>().Where(a => a.EntityType == entityType && a.EntityId == entityId);

    private async Task<Result<IReadOnlyList<ExtendedAttributeDto>>> LoadAsync(string entityType, Guid entityId, CancellationToken cancellationToken) =>
        (await Attributes(entityType, entityId).AsNoTracking().OrderBy(a => a.Group).ThenBy(a => a.Key).ToListAsync(cancellationToken)).Select(ToDto).ToList();

    private static void Apply(ExtendedAttribute attribute, ExtendedAttributeDto dto)
    {
        attribute.Type = dto.Type;
        attribute.Text = dto.Type == ExtendedAttributeType.Text ? dto.Text : null;
        attribute.Decimal = dto.Type == ExtendedAttributeType.Decimal ? dto.Decimal : null;
        attribute.DateTime = dto.Type == ExtendedAttributeType.DateTime ? dto.DateTime : null;
        attribute.Json = dto.Type == ExtendedAttributeType.Json ? dto.Json : null;
        attribute.Group = dto.Group;
        attribute.Description = dto.Description;
        attribute.ExternalId = dto.ExternalId;
        attribute.IsActive = dto.IsActive;
    }

    private static ExtendedAttributeDto ToDto(ExtendedAttribute a) => new()
    {
        Id = a.Id,
        Key = a.Key,
        Type = a.Type,
        Text = a.Text,
        Decimal = a.Decimal,
        DateTime = a.DateTime,
        Json = a.Json,
        Group = a.Group,
        Description = a.Description,
        ExternalId = a.ExternalId,
        IsActive = a.IsActive,
    };
}
