using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetRoles : IQuery<Result<IReadOnlyList<RoleDto>>>;
