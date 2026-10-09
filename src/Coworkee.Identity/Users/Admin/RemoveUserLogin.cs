using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Unlinks an external sign-in from a user.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record RemoveUserLogin(Guid Id, string LoginProvider, string ProviderKey) : ICommand<Result>;
