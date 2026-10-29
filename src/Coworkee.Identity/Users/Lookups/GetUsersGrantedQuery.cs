using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Paging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Lookups;

/// <summary>Users of the own organisation who hold <paramref name="Permission"/>, optionally only members of <paramref name="GroupId"/> and its sub-groups (<see cref="IPermissionChecker.UsersGrantedAsync"/>).</summary>
[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUsersGrantedQuery(string Permission, Guid? GroupId, PageRequest Page) : IQuery<Result<PagedResult<UserDto>>>;

internal sealed class GetUsersGrantedValidator : AbstractValidator<GetUsersGrantedQuery>
{
    public GetUsersGrantedValidator(IPermissionDefinitionManager definitions)
    {
        RuleFor(q => q.Permission).NotEmpty().Must(definitions.Exists).WithMessage("Unknown permission.");
        RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
    }
}

internal sealed class GetUsersGrantedHandler(IPermissionChecker permissions, CoworkeeDbContext db) : IHandler<GetUsersGrantedQuery, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsersGrantedQuery query, CancellationToken cancellationToken)
    {
        if (query.GroupId is { } groupId && !await db.Set<UserGroup>().AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return GroupErrors.NotFound;
        }

        var page = await permissions.UsersGrantedAsync(query.Permission, query.GroupId, query.Page, cancellationToken);
        var ids = page.Items.ToList();
        var rows = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var roles = await UserRoles.ForAsync(db, ids, cancellationToken);
        return new PagedResult<UserDto>([.. ids.Where(rows.ContainsKey).Select(id => rows[id].ToDto(roles))], page.TotalCount, page.Page, page.PageSize);
    }
}
