using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Account.Email;

/// <summary>Changes a user's address at once; unconfirmed addresses get a confirmation mail, the previous address a notice.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserEmail(Guid Id, string Email, bool Confirmed) : ICommand<Result>;
