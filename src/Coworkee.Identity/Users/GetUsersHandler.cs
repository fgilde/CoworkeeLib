using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class GetUsersHandler(CoworkeeDbContext db, ICurrentUser currentUser, AdminReach reach) : IHandler<GetUsers, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsers query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var users = await reach.FilterUsersAsync(db.Set<User>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId), cancellationToken);
        if (query.RoleId is { } roleId)
        {
            users = users.Where(u => db.Set<IdentityUserRole<Guid>>().Any(r => r.UserId == u.Id && r.RoleId == roleId));
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            users = users.Where(u => u.Email!.ToLower().Contains(search)
                                     || (u.FirstName ?? string.Empty).ToLower().Contains(search)
                                     || (u.LastName ?? string.Empty).ToLower().Contains(search));
        }

        var total = await users.CountAsync(cancellationToken);
        var rows = await users.OrderBy(u => u.Email).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken);
        var roles = await UserRoles.ForAsync(db, rows.Select(u => u.Id).ToList(), cancellationToken);
        return new PagedResult<UserDto>(rows.Select(u => u.ToDto(roles)).ToList(), total, page.Page, page.PageSize);
    }
}
