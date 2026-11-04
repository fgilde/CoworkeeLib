using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class GetCustomPermissionsHandler(CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants)
    : IHandler<GetCustomPermissions, Result<IReadOnlyList<CustomPermissionDto>>>
{
    public async Task<Result<IReadOnlyList<CustomPermissionDto>>> HandleAsync(GetCustomPermissions query, CancellationToken cancellationToken)
    {
        if (!await CustomPermissionRules.IsHostAsync(currentUser, tenants, cancellationToken))
        {
            return CustomPermissionRules.HostOnly;
        }

        return await db.Set<CustomPermission>().AsNoTracking()
            .OrderBy(p => p.Group).ThenBy(p => p.Name)
            .Select(p => new CustomPermissionDto(p.Name, p.DisplayName, p.Description, p.Group))
            .ToListAsync(cancellationToken);
    }
}
