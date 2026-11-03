using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Lookups;

/// <summary>Users of the own organisation who hold <paramref name="Permission"/>, optionally only members of <paramref name="GroupId"/> and its sub-groups (<see cref="IPermissionChecker.UsersGrantedAsync"/>).</summary>
// who holds what tells as much as the roles page: both permissions
[RequiresPermission(IdentityPermissions.Users.View)]
[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetUsersGrantedQuery(string Permission, Guid? GroupId, PageRequest Page) : IQuery<Result<PagedResult<UserDto>>>;
