using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Sets a user's password; the password rules apply and the user's sessions end.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
[AiTool(Exclude = true)]
public sealed record SetUserPassword(Guid Id, string Password, bool MustChangePassword) : ICommand<Result>;
