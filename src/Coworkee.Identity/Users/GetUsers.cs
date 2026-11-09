using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
/// <summary>A page of the users; <paramref name="RoleId"/> keeps those who have the role directly.</summary>
public sealed record GetUsers(PageRequest Page, Guid? RoleId = null) : IQuery<Result<PagedResult<UserDto>>>;
