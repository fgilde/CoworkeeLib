using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Account;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SendPasswordReset(Guid UserId) : ICommand<Result>;
