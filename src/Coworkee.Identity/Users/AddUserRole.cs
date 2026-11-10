using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

/// <summary>Gives a user one more role; his other roles stay, so two administrators editing the same user do not undo each other's changes.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record AddUserRole(Guid Id, Guid RoleId) : ICommand<Result>;
