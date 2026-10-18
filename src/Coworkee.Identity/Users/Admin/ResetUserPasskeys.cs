using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Removes every passkey of a user, e.g. after a lost device, and ends the user's sessions.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record ResetUserPasskeys(Guid Id) : ICommand<Result>;
