using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class GetGroupsHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<GetGroups, Result<PagedResult<GroupDto>>>
{
    public async Task<Result<PagedResult<GroupDto>>> HandleAsync(GetGroups query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var groups = await reach.FilterGroupsAsync(db.Set<UserGroup>().AsNoTracking().AsSplitQuery(), cancellationToken);
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            groups = groups.Where(g => g.Name.ToLower().Contains(search));
        }

        var total = await groups.CountAsync(cancellationToken);
        var items = await groups.OrderBy(g => g.Name).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.Members.Select(m => m.UserId).ToList(), g.Roles.Select(r => r.RoleId).ToList(), g.ParentId, g.Code,
                g.Members.Where(m => m.IsOwner).Select(m => m.UserId).ToList(), g.IsDefault))
            .ToListAsync(cancellationToken);
        return new PagedResult<GroupDto>(items, total, page.Page, page.PageSize);
    }
}
