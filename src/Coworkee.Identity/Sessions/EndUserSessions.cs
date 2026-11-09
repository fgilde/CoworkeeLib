using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Sessions;

/// <summary>Ends every session of a user, like "Sign out everywhere" (<see cref="Users.SignOutUser"/>), for holders of the session permission.</summary>
[RequiresPermission(IdentityPermissions.Sessions.Manage)]
public sealed record EndUserSessions(Guid UserId) : ICommand<Result>;
