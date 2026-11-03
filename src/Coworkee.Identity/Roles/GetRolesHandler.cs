using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

internal sealed class GetRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetRoles, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(GetRoles query, CancellationToken cancellationToken) =>
        await db.Set<Role>().AsNoTracking()
            .Where(r => r.TenantId == null || r.TenantId == currentUser.TenantId)
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.IsSystem, r.SelectableForRegistration, r.IsDefault))
            .ToListAsync(cancellationToken);
}
