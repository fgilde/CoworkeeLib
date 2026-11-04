using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record UpdateCustomPermission(string Name, UpdateCustomPermissionRequest Permission) : ICommand<Result>;
