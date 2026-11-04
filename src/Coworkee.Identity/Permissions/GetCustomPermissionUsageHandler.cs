using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class GetCustomPermissionUsageHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants, IPermissionDefinitionManager definitions, CustomPermissionDefinitions stored)
    : IHandler<GetCustomPermissionUsage, Result<CustomPermissionUsageDto>>
{
    public async Task<Result<CustomPermissionUsageDto>> HandleAsync(GetCustomPermissionUsage query, CancellationToken cancellationToken)
    {
        var found = await CustomPermissionRules.FindAsync(db, currentUser, tenants, definitions, stored, query.Name, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        // the permission is defined everywhere, so its grants in other organisations go as well; their roles carry the organisation's name
        var grants = db.Set<PermissionGrant>().Where(g => g.Name == query.Name);
        var roles = await (from role in db.Set<Role>()
                           join tenant in db.Set<Tenant>() on role.TenantId equals tenant.Id into owners
                           from owner in owners.DefaultIfEmpty()
                           where grants.Any(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == role.Id)
                           select new { role.Name, role.TenantId, Tenant = owner == null ? null : owner.Name })
            .ToListAsync(cancellationToken);
        var groups = await db.Set<UserGroup>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter])
            .Where(g => grants.Any(x => x.ProviderType == PermissionProviderType.Group && x.ProviderKey == g.Id))
            .Select(g => g.Name).ToListAsync(cancellationToken);
        var users = await db.Set<User>()
            .Where(u => grants.Any(x => x.ProviderType == PermissionProviderType.User && x.ProviderKey == u.Id))
            .Select(u => u.Email!).ToListAsync(cancellationToken);

        return new CustomPermissionUsageDto(
            [.. roles.Select(r => r.TenantId == currentUser.TenantId || r.Tenant is null ? r.Name! : $"{r.Name} ({r.Tenant})").Order(StringComparer.CurrentCulture)],
            [.. groups.Order(StringComparer.CurrentCulture)],
            [.. users.Order(StringComparer.OrdinalIgnoreCase)]);
    }
}
