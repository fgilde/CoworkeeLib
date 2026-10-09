using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Turns two-step verification off and forgets the authenticator and recovery codes, e.g. after a lost phone.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record ResetUserTwoFactor(Guid Id) : ICommand<Result>;
