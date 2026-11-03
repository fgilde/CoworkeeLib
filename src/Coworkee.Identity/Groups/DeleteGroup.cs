using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record DeleteGroup(Guid Id) : ICommand<Result>;
