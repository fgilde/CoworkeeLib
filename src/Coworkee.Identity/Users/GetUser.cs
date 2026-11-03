using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUser(Guid Id) : IQuery<Result<UserDetailDto>>;
