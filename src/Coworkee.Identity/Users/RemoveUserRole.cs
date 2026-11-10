using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

/// <summary>Takes one role from a user; a role he does not have is no error, so a repeated click stays harmless.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record RemoveUserRole(Guid Id, Guid RoleId) : ICommand<Result>;
