using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Account.Users;

/// <summary>Sets the language a user sees and gets mails in; null follows the organisation.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserLanguage(Guid Id, string? Culture) : ICommand<Result>;
