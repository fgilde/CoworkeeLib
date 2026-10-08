using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Lookups;

internal sealed class GetUsersRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser)
    : IHandler<GetUsersRolesQuery, Result<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>>>
{
    public async Task<Result<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>>> HandleAsync(GetUsersRolesQuery query, CancellationToken cancellationToken)
    {
        var ids = await db.Set<User>().Where(u => query.Ids.Contains(u.Id) && u.TenantId == currentUser.TenantId).Select(u => u.Id).ToListAsync(cancellationToken);
        var roles = await UserRoles.ForAsync(db, ids, cancellationToken);
        return Result<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>>.Success(
            ids.ToDictionary(id => id, id => (IReadOnlyList<RoleRefDto>)(roles.GetValueOrDefault(id) ?? [])));
    }
}
