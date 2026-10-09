using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Account.Users;

/// <summary>Invites a user by mail to choose a password.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SendInvitation(Guid Id) : ICommand<Result>;
