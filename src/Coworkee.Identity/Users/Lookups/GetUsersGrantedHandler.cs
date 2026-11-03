using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Lookups;

internal sealed class GetUsersGrantedHandler(IPermissionChecker permissions, CoworkeeDbContext db, AdminReach reach, IServiceProvider services)
    : IHandler<GetUsersGrantedQuery, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsersGrantedQuery query, CancellationToken cancellationToken)
    {
        if (query.GroupId is { } groupId && (!await db.Set<UserGroup>().AnyAsync(g => g.Id == groupId, cancellationToken) || !await reach.ReachesGroupAsync(groupId, cancellationToken)))
        {
            return GroupErrors.NotFound;
        }

        // a group-scoped administrator learns who holds it among the users of his reach; the lib's checker also when the app decorates it
        var page = await reach.UserFilterAsync(cancellationToken) is { } within
            ? await (permissions as Permissions.PermissionChecker ?? Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Permissions.PermissionChecker>(services))
                .UsersGrantedAsync(query.Permission, query.GroupId, query.Page, cancellationToken, within)
            : await permissions.UsersGrantedAsync(query.Permission, query.GroupId, query.Page, cancellationToken);
        var ids = page.Items.ToList();
        var rows = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var roles = await UserRoles.ForAsync(db, ids, cancellationToken);
        return new PagedResult<UserDto>([.. ids.Where(rows.ContainsKey).Select(id => rows[id].ToDto(roles))], page.TotalCount, page.Page, page.PageSize);
    }
}
