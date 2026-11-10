using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

/// <summary>The permissions of a user with their sources (role, role through a group, group grant, direct grant, computed) and his direct grants.</summary>
[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUserPermissions(Guid UserId) : IQuery<Result<UserPermissionsDto>>;
