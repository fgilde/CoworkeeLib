using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserRoles(Guid Id, IReadOnlyList<Guid> RoleIds) : ICommand<Result>;
