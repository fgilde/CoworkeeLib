using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Sets or (with null) removes a user's profile picture.</summary>
[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserAvatar(Guid Id, string? DataUrl) : ICommand<Result>;
