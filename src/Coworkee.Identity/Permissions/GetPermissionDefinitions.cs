using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetPermissionDefinitions : IQuery<Result<IReadOnlyList<PermissionGroupDto>>>;
