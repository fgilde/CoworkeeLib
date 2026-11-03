using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record UpdateRole(Guid Id, RoleRequest Role) : ICommand<Result>;
