using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing;

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

        if (query.ImpersonatorId is { } impersonator)
        {
            entries = entries.Where(e => e.ImpersonatorId == impersonator);
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
                e.OccurredAt, e.CorrelationId, e.Changes.Select(c => new AuditChangeDto(c.Property, c.OldValue, c.NewValue)).ToList(), e.ImpersonatorId)).ToList(),
            total, page, pageSize);
    }
}
