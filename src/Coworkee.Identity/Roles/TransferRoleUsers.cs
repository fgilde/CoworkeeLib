using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

/// <summary>Copies or moves the users of a role into other roles (<see cref="TransferRoleUsersRequest"/>); the number of users it took.</summary>
// the same change as SetUserRoles for many users at once, so the same permission
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record TransferRoleUsers(Guid RoleId, TransferRoleUsersRequest Request) : ICommand<Result<int>>;
