using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record DeleteRole(Guid Id) : ICommand<Result>;
