using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUsers(PageRequest Page) : IQuery<Result<PagedResult<UserDto>>>;
